/*  @file Iso14443a.cs
 *  @brief Mercury API - Iso14443a tag information
 *  @author Bindhu Priya Chanda
 *  @date 1/30/2020
 * Copyright (c) 2020 Jadak, a business unit of Novanta Corporation.
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
using System;
using System.Collections.Generic;

namespace ThingMagic
{
    /// <summary>
    /// Iso14443A tag-specific constructs
    /// </summary>
    public static class Iso14443a
    {
        #region TagType

        /// <summary>
        /// enum to define variants of tag type under ISO14443A protocol
        /// </summary>
        [Flags]
        public enum TagType
        {
            /// <summary>
            /// Auto detect - supports all tag types
            /// </summary>
            AUTO_DETECT = 0x00000001,
            /// <summary>
            /// Mifare Plus tag type
            /// </summary>
            MIFARE_PLUS = 0x00000002,
            /// <summary>
            /// Mifare Ultralight tag type
            /// </summary>
            MIFARE_ULTRALIGHT = 0x00000004,
            /// <summary>
            /// Mifare Classic tag type
            /// </summary>
            MIFARE_CLASSIC = 0x00000008,
            /// <summary>
            /// NXP Ntag tag type
            /// </summary>
            NTAG = 0x00000010,
            /// <summary>
            /// Mifare Desfire tag type
            /// </summary>
            MIFARE_DESFIRE = 0x00000020,
            /// <summary>
            /// Mifare Mini tag type
            /// </summary>
            MIFARE_MINI = 0x00000040,
            /// <summary>
            /// Ultralight NTAG tag type
            /// </summary>
            ULTRALIGHT_NTAG = 0x00000080
        }
        #endregion

        #region TagData

        /// <summary>
        /// ISO14443A specific version of TagData.
        /// </summary>
        public class TagData : ThingMagic.TagData
        {
            #region Properties

            /// <summary>
            /// Tag's RFID protocol
            /// </summary>
            public override TagProtocol Protocol
            {
                get
                {
                    return TagProtocol.ISO14443A;
                }
            }

            #endregion

            #region Construction

            /// <summary>
            /// Create TagData with blank CRC
            /// </summary>
            /// <param name="uidBytes">UID value</param>
            public TagData(ICollection<byte> uidBytes) : base(uidBytes) { }

            #endregion
        }

        #endregion

    }
}