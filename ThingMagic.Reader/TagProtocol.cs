/*
 * Copyright (c) 2009 ThingMagic, Inc.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

namespace ThingMagic
{
    /// <summary>
    /// Tag protocol designator
    /// </summary>
    public enum TagProtocol
    {
        /// <summary>
        /// No protocol selected
        /// </summary>
        NONE = 0,
        /// <summary>
        /// Gen2
        /// </summary>
        GEN2 = 5,
        /// <summary>
        /// ISO18000-6B
        /// </summary>
        ISO180006B = 3,
        /// <summary>
        /// ISO18000-6B with UCODE extension for >64-bit EPCs
        /// </summary>
        ISO180006B_UCODE = 6,
        /// <summary>
        /// IPX at 64kHz
        /// </summary>
        IPX64 = 7,
        /// <summary>
        /// IPX at 256kHz
        /// </summary>
        IPX256 = 8,
        /// <summary>
        /// ATA
        /// </summary>
        ATA = 0x1D,
        /// <summary>
        /// ISO14443A protocol for the HF reader
        /// </summary>
        ISO14443A = 0x09,
        /// <summary>
        /// ISO14443B protocol for the HF reader
        /// </summary>
        ISO14443B = 0x0A,
        /// <summary>
        /// ISO15693 protocol for the HF reader
        /// </summary>
        ISO15693 = 0x0B,
        /// <summary>
        /// ISO18092 protocol for the HF reader
        /// </summary>
        ISO18092 = 0x0C,
        /// <summary>
        /// FELICA protocol for the HF reader
        /// </summary>
        FELICA = 0x0D,
        /// <summary>
        /// ISO180003M3 protocol for the HF reader
        /// </summary>
        ISO180003M3 = 0x0E,
        /// <summary>
        ///  LF125KHZ protocol for the LF reader
        /// </summary>
        LF125KHZ = 0x14,
        /// <summary>
        ///  LF134KHZ protocol for the LF reader
        /// </summary>
        LF134KHZ = 0x15,
    };
}