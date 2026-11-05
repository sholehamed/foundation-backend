using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Domain.Common;
using Modules.System.FileStorage.Domain.Entities;
using Modules.System.FileStorage.Infrastructure.Persistence.Contexts;
using System.Data;

namespace Modules.System.FileStorage.Infrastructure.Persistence.Repositories
{
    public class MediaRepository : IMediaRepository
    {
        private readonly AppDbContext _context;

        public MediaRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Media media, CancellationToken cancellationToken = default)
        {
            await _context.Media.AddAsync(media, cancellationToken);
        }

        public Task<Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return _context.Media
                .Include(x => x.Binaries)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public Task<MediaBinary?> GetBinaryAsync(Guid mediaId, MediaVariantType variantType, CancellationToken cancellationToken = default)
        {
            return _context.MediaBinaries
                .FirstOrDefaultAsync(x => x.MediaId == mediaId && x.VariantType == variantType, cancellationToken);
        }

        public async Task AddBinaryAsync(MediaBinary binary, CancellationToken cancellationToken = default)
        {
            await _context.MediaBinaries.AddAsync(binary, cancellationToken);
        }

        public async Task DeleteVariantsAsync(Guid mediaId, params MediaVariantType[] variants)
        {
            var binaries = await _context.MediaBinaries
                .Where(x => x.MediaId == mediaId && variants.Contains(x.VariantType))
                .ToListAsync();

            _context.MediaBinaries.RemoveRange(binaries);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _context.SaveChangesAsync(cancellationToken);
        }
        public async Task<MediaDownloadResult?> OpenDownloadStreamAsync(
        Guid mediaId,
        MediaVariantType variantType,
        CancellationToken cancellationToken = default)
        {
            var connectionString = _context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Database connection string not found.");

            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
            SELECT TOP(1)
                FileName,
                ContentType,
                FileSize,
                Data
            FROM MediaBinary
            WHERE MediaId = @MediaId AND VariantType = @VariantType
            """;

            var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@MediaId", SqlDbType.UniqueIdentifier).Value = mediaId;
            command.Parameters.Add("@VariantType", SqlDbType.Int).Value = (int)variantType;

            var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow,
                cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await command.DisposeAsync();
                await connection.DisposeAsync();
                return null;
            }

            var fileName = reader.IsDBNull(0) ? "file.bin" : reader.GetString(0);
            var contentType = reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1);
            var fileSize = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);

            var sqlStream = reader.GetStream(3);

            return new MediaDownloadResult
            {
                FileName = fileName,
                ContentType = contentType,
                ContentLength = fileSize,
                Content = new SqlDataReaderStream(sqlStream, reader, command, connection)
            };
        }
    }
}
