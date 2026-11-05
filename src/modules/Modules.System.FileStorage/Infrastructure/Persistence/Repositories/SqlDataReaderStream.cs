using Microsoft.Data.SqlClient;

namespace Modules.System.FileStorage.Infrastructure.Persistence.Repositories
{
    internal sealed class SqlDataReaderStream : Stream
    {
        private readonly Stream _inner;
        private readonly SqlDataReader _reader;
        private readonly SqlCommand _command;
        private readonly SqlConnection _connection;

        public SqlDataReaderStream(
            Stream inner,
            SqlDataReader reader,
            SqlCommand command,
            SqlConnection connection)
        {
            _inner = inner;
            _reader = reader;
            _command = command;
            _connection = connection;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _reader.Dispose();
                _command.Dispose();
                _connection.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await _reader.DisposeAsync();
            await _command.DisposeAsync();
            await _connection.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
