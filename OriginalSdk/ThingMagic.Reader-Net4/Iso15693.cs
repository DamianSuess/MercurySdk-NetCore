/*  @file Iso15693.cs
 *  @brief Mercury API - Iso15693 tag information
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
    /// Iso15693 tag-specific constructs
    /// </summary>
    public static class Iso15693
    {
        #region TagType

        /// <summary>
        /// enum to define variants of tag type under ISO15693 protocol
        /// </summary>
        [Flags]
        public enum TagType
        {
            /// <summary>
            /// Auto detect - supports all tag types
            /// </summary>
            AUTO_DETECT = 0x00000001,
            /// <summary>
            /// HID Iclass SE tagtype
            /// </summary>
            HID_ICLASS_SE = 0x00000100,
            /// <summary>
            /// NXP Icode SLI tagtype
            /// </summary>
            ICODE_SLI = 0x00000200,
            /// <summary>
            /// NXP Icode SLI-L tagtype
            /// </summary>
            ICODE_SLI_L = 0x00000400,
            /// <summary>
            /// NXP Icode SLI-S tag type
            /// </summary>
            ICODE_SLI_S = 0x00000800,
            /// <summary>
            /// NXP ICODE DNA tagtype
            /// </summary>
            ICODE_DNA = 0x00001000,
            /// <summary>
            /// NXP ICODE SLIX tagtype
            /// </summary>
            ICODE_SLIX = 0x00002000,
            /// <summary>
            /// NXP ICODE SLIX-L tagtype
            /// </summary>
            ICODE_SLIX_L = 0x00004000,
            /// <summary>
            /// NXP ICODE SLIX-S tagtype
            /// </summary>
            ICODE_SLIX_S = 0x00008000,
            /// <summary>
            /// NXP Icode SLIX-2 tagtype
            /// </summary>
            ICODE_SLIX_2 = 0x00010000,
        }
        #endregion

        #region TagData

        /// <summary>
        /// ISO15693 specific version of TagData.
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
                    return TagProtocol.ISO15693;
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