using System;
using System.Device.I2c;

namespace InvisiblePlayer.Raspi.Hardware
{
    public class Mcp23016Controller : IDisposable
    {
        private readonly I2cDevice _i2cDevice;

        // Adresy registrů MCP23016
        private const byte GP0 = 0x00; // Port 0 (Inputs/Outputs)
        private const byte GP1 = 0x01; // Port 1 (Inputs/Outputs)
        private const byte IODIR0 = 0x06; // Direction Port 0 (0 = Output, 1 = Input)
        private const byte IODIR1 = 0x07; // Direction Port 1

        public Mcp23016Controller(int busId, int deviceAddress)
        {
            var settings = new I2cConnectionSettings(busId, deviceAddress);
            _i2cDevice = I2cDevice.Create(settings);

            // Nastavení: Port 0 jako Vstupy (klávesy/sklopky), Port 1 jako Výstupy (LED podsvícení)
            _i2cDevice.Write(new byte[] { IODIR0, 0xFF }); // All inputs
            _i2cDevice.Write(new byte[] { IODIR1, 0x00 }); // All outputs
        }

        // Čtení stavu tónových kláves / sklopek (16 bitů)
        public ushort ReadKeyInputs()
        {
            Span<byte> writeBuffer = stackalloc byte[] { GP0 };
            Span<byte> readBuffer = stackalloc byte[2];

            _i2cDevice.WriteRead(writeBuffer, readBuffer);

            // Vrací bitovou masku 16 stisknutých/sepnutých kontaktů
            return (ushort)(readBuffer[0] | (readBuffer[1] << 8));
        }

        // Nastavení LED podsvícení sklopek
        public void SetStopLedBacklight(byte ledMask)
        {
            _i2cDevice.Write(new byte[] { GP1, ledMask });
        }

        private bool _disposed;

        public void Dispose()
        {
            // OPRAVA L6: '_i2cDevice?.Dispose()' mělo mrtvý '?.' - pole je readonly,
            // non-nullable a přiřazené v konstruktoru. Chyběl naopak guard proti
            // dvojímu Dispose (I2cDevice by na druhé volání mohl sáhnout na už
            // uvolněný handle) a GC.SuppressFinalize (CA1816).
            if (_disposed) return;
            _disposed = true;

            _i2cDevice.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}