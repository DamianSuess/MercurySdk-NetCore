
/*  @file Lf125Khz.cs
 *  @brief Mercury API - Lf125Khz tag information and interfaces
 *  @author Bindhu Priya Chanda
 *  @date 2/24/2020

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
    /// Lf125khz tag-specific constructs
    /// </summary>
    public static class Lf125khz
    {
        #region TagType

        /// <summary>
        /// enum to define variants of tag type under LF125KHZ protocol
        /// </summary>
        [Flags]
        public enum TagType
        {
            /// <summary>
            /// Auto detect - supports all tag types
            /// </summary>
            AUTO_DETECT = 0x00000001,
            /// <summary>
            /// HID PROX II tag type
            /// </summary>
            HID_PROX = 0x01000000,
            /// <summary>
            /// AWID tag type
            /// </summary>
            AWID = 0x02000000,
            /// <summary>
            /// KERI tag type
            /// </summary>
            KERI = 0x04000000,
            /// <summary>
            /// INDALA tag type
            /// </summary>
            INDALA = 0x08000000,
            /// <summary>
            /// NXP HITAG 2 tag type
            /// </summary>
            HITAG_2 = 0x10000000,
            /// <summary>
            /// NXP HITAG 1 tag type
            /// </summary>
            HITAG_1 = 0x20000000,
            /// <summary>
            /// EM4100 tag type
            /// </summary>
            EM_4100 = 0x40000000
        }
        #endregion

        #region TagData

        /// <summary>
        /// Lf125Khz specific version of TagData.
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
                    return TagProtocol.LF125KHZ;
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

        #region Secure read
        /// <summary>
        /// Enum to select Secure read format
        /// </summary>
        [Flags]
        public enum NHX_Type
        {
            /// <summary>
            /// NO TYPE
            /// </summary>
            TYPE_NONE = 0,
            /// <summary>
            /// TYPE_10022
            /// </summary>
            TYPE_10022 = 1
        }
        #endregion
    }
}
