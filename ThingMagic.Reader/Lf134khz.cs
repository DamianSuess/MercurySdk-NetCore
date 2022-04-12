/*  @file Lf134Khz.cs
 *  @brief Mercury API - Lf134Khz tag information and interfaces
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
    /// LF134KHZ tag-specific constructs
    /// </summary>
    public static class Lf134khz
    {
        #region TagType

        /// <summary>
        /// enum to define variants of tag type under LF134KHZ protocol
        /// </summary>
        [Flags]
        public enum TagType
        {
            /// <summary>
            /// Auto detect - supports all tag types
            /// </summary>
            AUTO_DETECT = 0x00000001,
        }

        #endregion

        #region TagData

        /// <summary>
        /// LF134KHZ specific version of TagData.
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
                    return TagProtocol.LF134KHZ;
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
