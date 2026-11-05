namespace Domain.Sharedkernel.Util
{
    using System.Globalization;
    using System.Security.Cryptography;

    public static class StructuredGuidV7
    {
        private const ushort MaxTableCode = 0x0FFF;

        private const ulong MaxPayload =
            0x3FFF_FFFF_FFFF_FFFFUL;

        private const ulong RfcVariant =
            0x8000_0000_0000_0000UL;

     

        /// <summary>
        /// Generates a runtime UUID v7 with an explicit timestamp.
        /// Primarily useful for tests.
        /// </summary>
        public static Guid Create(
            ushort table)
        {
            ulong randomPayload = CreateSecureRandomPayload();

            return CreateCore(
                TimeProvider.UtcNow,
                (ushort)table,
                randomPayload);
        }

        /// <summary>
        /// Generates a deterministic UUID v7.
        /// Suitable for stable seed data.
        /// </summary>
        public static Guid CreateDeterministic(
            ushort table,
            ulong sequence)
        {
            return CreateCore(
                TimeProvider.UtcNowOffset,
                (ushort)table,
                sequence);
        }

        private static Guid CreateCore(
            DateTimeOffset timestamp,
            ushort tableCode,
            ulong payload)
        {
            ValidateTableCode(tableCode);
            ValidatePayload(payload);

            long unixMilliseconds =
                timestamp.ToUnixTimeMilliseconds();

            if (unixMilliseconds < 0 ||
                unixMilliseconds > 0xFFFF_FFFF_FFFFL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp),
                    timestamp,
                    "Timestamp must fit within the 48-bit UUID v7 timestamp.");
            }

            ulong variantAndPayload =
                RfcVariant | payload;

            string raw =
                unixMilliseconds.ToString(
                    "x12",
                    CultureInfo.InvariantCulture) +
                "7" +
                tableCode.ToString(
                    "x3",
                    CultureInfo.InvariantCulture) +
                variantAndPayload.ToString(
                    "x16",
                    CultureInfo.InvariantCulture);

            string formatted =
                $"{raw[..8]}-" +
                $"{raw[8..12]}-" +
                $"{raw[12..16]}-" +
                $"{raw[16..20]}-" +
                $"{raw[20..32]}";

            return Guid.ParseExact(formatted, "D");
        }

        private static ulong CreateSecureRandomPayload()
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];

            RandomNumberGenerator.Fill(bytes);

            ulong value =
                ((ulong)bytes[0] << 56) |
                ((ulong)bytes[1] << 48) |
                ((ulong)bytes[2] << 40) |
                ((ulong)bytes[3] << 32) |
                ((ulong)bytes[4] << 24) |
                ((ulong)bytes[5] << 16) |
                ((ulong)bytes[6] << 8) |
                bytes[7];

            return value & MaxPayload;
        }

        private static void ValidateTableCode(
            ushort tableCode)
        {
            if (tableCode == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableCode),
                    tableCode,
                    "Table code 0 is reserved.");
            }

            if (tableCode > MaxTableCode)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableCode),
                    tableCode,
                    $"Table code must not exceed {MaxTableCode}.");
            }
        }

        private static void ValidatePayload(
            ulong payload)
        {
            if (payload > MaxPayload)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    payload,
                    $"Payload must not exceed {MaxPayload}.");
            }
        }
    }

}
