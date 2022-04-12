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

using System;
using System.Collections;
using System.IO;
using System.IO.Ports;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ThingMagic
{
    /// <summary>
    /// The SerialReader class is an implementation of a Reader object
    /// that communicates with a ThingMagic embedded RFID module via the embedded module serial protocol. 
    /// In addition to the Reader interface, direct access to the commands of the embedded module 
    /// serial protocol is supported.
    /// 
    /// 
    /// Instances of the SerialReader class are created with the {@link
    /// #com.thingmagic.Reader.create} method with a "eapi" URI or a
    /// generic "tmr" URI that references a local serial port.
    /// </summary>  
    public sealed class SerialReader : Reader
    {
        #region Constants

        private const int LENGTH_RESPONSE_OFFSET = 1;
        private const int OPCODE_RESPONSE_OFFSET = 2;
        private const int STATUS_RESPONSE_OFFSET = 3;
        private const int ARGS_RESPONSE_OFFSET = 5;
        private const int TAG_RECORD_LENGTH = 18;
        private const int MAX_TAGS_PER_MESSAGE = 13;

        /*Reader Hardware Version Number*/
        private const byte MODEL_M5E = 0x00;
        private const byte MODEL_M5E_COMPACT = 0x01;
        private const byte MODEL_M4E = 0x03;
        private const byte MODEL_M6E = 0x18;
        private const byte MODEL_M6E_I = 0x19;
        private const byte MODEL_MICRO = 0x20;
        private const byte MODEL_M6E_MICRO = 0x01;
        private const byte MODEL_M6E_MICRO_USB = 0x02;
        private const byte MODEL_M6E_MICRO_USB_PRO = 0x03;
        private const byte MODEL_M6E_NANO = 0x30;
        private const byte MODEL_M3E = 0x80;

        private const byte MODEL_M3E_I_REV1 = 0x01;

        private const byte MODEL_M6E_I_REV1 = 0x01;
        private const byte MODEL_M6E_I_PRC = 0x02;
        private const byte MODEL_M6E_I_JIC = 0x03;

        private const byte MODEL_M5E_I = 0x02; // I - International
        private const byte MODEL_M5E_I_REV_EU = 0x01;
        private const byte MODEL_M5E_I_REV_NA = 0x02;
        private const byte MODEL_M5E_I_REV_JP = 0x03;
        private const byte MODEL_M5E_I_REV_PRC = 0x04;

        private const byte SINGULATION_FLAG_METADATA_ENABLED = 0x10;
        // constant for multi-select
        private const byte SINGULATION_OPTION_MULTIPLE_SELECT = 0x88;
        // constant for Read after write
        private const byte SINGULATION_OPTION_READ_AFTER_WRITE = 0x84;
        //Constants
        private const string StrFlushReads = "Flush Reads";
        private const string StrStopReading = "Stop Reading";
        private const uint ErrorCodePortDoesNotExist = 0x800703e3;
        // When exception received is Error: The port COMxx doesn't exist
        private const uint ErrorCodePort1DoesNotExist = 0x80131620;
        // Handling Socket IO exception
        private const uint ErrorCodeSocketIOException = 0x80131620;
        // Handling Win32 IO errors
        // 0x80070016---> The device does not recognize the command.
        // 0x8007001F---> A device attached to the system is not functioning.
        private const uint ErrorCodeDeviceNotRecognized = 0x80070016;
        private const uint ErrorCodeDeviceNotFunctioning = 0x8007001F;
        private bool enableMultipleSelect = false;
        private bool isMultipleSelectSupported = false;
        private static bool isEmbeddedTagOp = false;
        private static bool enableReadAfterWrite = false;
        private static bool isProtocolDynamicSwitching = false;
        // Flag if set to true indicates perAntenna time is set, API will use the antenna time(On/Off time) as mentioned in the TMR_PARAM_PER_ANTENNA_TIME param.
        private bool isPerAntTimeSet = false;
        RegulatoryMode rMode = RegulatoryMode.TIMED;
        RegulatoryModulation rModulation = RegulatoryModulation.CW;
        int regOnTime = 500;
        int regOffTime = 0;
        int subOffTimeout = 0;
        int transpTimeout = 0;
        bool isSubOffTime = false;
        bool isM6eVariant = false;
        bool isTxRxMapSet = false;
        static List<ReaderFeaturesFlag> featureflag = new List<ReaderFeaturesFlag>();
        Dictionary<int, int[]> defaultForwardMap = new Dictionary<int, int[]>();
        Dictionary<byte, int> defaultReverseMap = new Dictionary<byte, int>();
        bool isValidationSuccess = false;
        /// <summary>
        /// Get the supported Tag type for the reader.
        /// </summary>
        public Dictionary<TagProtocol, UInt64> protoSuppTagTypeList = new Dictionary<TagProtocol, UInt64>();
        /// <summary>
        /// Get the Supported Protocol
        /// </summary>
        public List<TagProtocol> validProtocols = new List<TagProtocol>();

        private const int LOGICAL_ANTENNAS_ALLOWED = 64;

        private int gpioNumber = 0;
        /// <summary>
        /// Flag to show if autonomous streaming is on
        /// </summary>
        public Boolean autonomousStreaming = false;

        private bool isResetReaderStatsSent = false;

	
        #endregion

        #region Constants property
        /// <summary>
        /// Enable/Disable Dynamic Protocol Switching
        /// </summary>
        public bool IsProtocolDynamicSwitching
        {
            get { return isProtocolDynamicSwitching; }
            set { isProtocolDynamicSwitching = value; }
        }
        #endregion

        #region Nested Enums

        #region ProductGroupID

        private enum ProductGroupID
        {
            MODULE = 0,
            RUGGEDIZED_READER = 1,
            USB_READER = 2,
            INVALID = 0xFFFF,
        }

        #endregion

        #region serialGen2LinkFrequency

        private enum serialGen2LinkFrequency
        {
            LINK250KHZ,

            LINK320KHZ,

            LINK640KHZ,
        }

        #endregion

        #region serialIso180006bLinkFrequency

        private enum serialIso180006bLinkFrequency
        {

            LINK40KHZ = 1,

            LINK160KHZ = 0,
        }

        #endregion

        #region serialIso180006bModulationdepth

        private enum serialIso180006bModulationdepth
        {

            Modulation99percent = 0,

            Modulation11percent = 1,
        }

        #endregion

        #region serialIso180006bDelimiter

        private enum serialIso180006bDelimiter
        {

            Delimiter1 = 1,

            Delimiter4 = 4,
        }

        #endregion

        #region SingulationOptions

        /// <summary>
        /// enum to define different singulation options.
        /// </summary>
        public enum SingulationOptions
        {
            /// <summary>
            /// UID based filter
            /// </summary>
            SELECT_ON_UID = 0x01,
            /// <summary>
            /// TagType based filter
            /// </summary>
            SELECT_ON_TAGTYPE = 0x02,
        }
        #endregion

        #region SerialRegion

        /// <summary>
        /// Regulatory region identifiers, as used in Get Current Region (67h)
        /// Each enum is assigned the byte value to be sent in M5e command.
        /// </summary>
        private enum SerialRegion
        {
            /// <summary>
            /// None, Region not Set
            /// </summary>
            NONE = 0x00,
            /// <summary>
            /// North America
            /// </summary>
            NA = 0x01,
            /// <summary>
            /// Europe, version 1 (LBT)
            /// </summary>
            EU = 0x02,
            /// <summary>
            /// Korea
            /// </summary>
            KR = 0x03,
            /// <summary>
            /// Korea (revised)
            /// </summary>
            KR2 = 0x09,
            /// <summary>
            /// India
            /// </summary>
            IN = 0x04,
            /// <summary>
            /// Japan
            /// </summary>
            JP = 0x05,
            /// <summary>
            /// China[, People's Republic of] (i.e., mainland China)
            /// </summary>
            PRC = 0x06,
            /// <summary>
            /// Europe, version 2 (??)
            /// </summary>
            EU2 = 0x07,
            /// <summary>
            /// Europe, version 3 (no LBT)
            /// </summary>
            EU3 = 0x08,
            // TODO: Get better descriptions of the different versions of EU
            /// <summary>
            /// PRC with 875KHZ 
            /// </summary>
            PRC2 = 0x0A,
            /// <summary>
            /// Australia
            /// </summary>
            AU = 0x0b,
            /// <summary>
            /// NewZealand
            /// </summary>
            NZ = 0x0c,
            /// <summary>
            /// Reduced FCC region
            /// </summary>
            NA2 = 13,
            /// <summary>
            /// NA3
            /// </summary>
            NA3 = 14,
            /// <summary>
            /// IS
            /// </summary>
            IS = 15,
            /// <summary>
            ///Malaysia 
            /// </summary>
            MY = 16,
            /// <summary>
            /// Indonesia
            /// </summary>
            ID = 17,
            /// <summary>
            /// Philippines 
            /// </summary>
            PH = 18,
            /// <summary>
            ///Taiwan
            /// </summary>
            TW = 19,
            /// <summary>
            ///   Macau
            /// </summary>
            MO = 20,
            /// <summary>
            ///   Russia
            /// </summary>
            RU = 21,
            /// <summary>
            /// Singapore 
            /// </summary>
            SG = 22,
            /// <summary>
            /// Japan 2
            /// </summary>
            JP2 = 23,
            /// <summary>
            /// Japan 3
            /// </summary>
            JP3 = 24,
            /// <summary>
            /// Vietnam
            /// </summary>
            VN = 25,
            /// <summary>
            /// Thailand
            /// </summary>
            TH = 26,
            /// <summary>
            /// Argentina
            /// </summary>
            AR = 27,
            /// <summary>
            /// Hongkong
            /// </summary>
            HK = 28,
            /// <summary>
            /// Bangladesh
            /// </summary>
            BD = 29,
            /// <summary>
            /// Europe, version 4 (4 channels (916.3MHz,17.5MHz,918.7MHz))
            /// </summary>
            EU4 = 30,
            /// <summary>
            /// Universal region is applicable for M3e product
            /// </summary>
            UNIVERSAL = 31,
            /// <summary>
            /// Israel2(IS2) region 
            /// </summary>
            IS2 = 32,
            /// <summary>
            /// NA4 region applicable for Micro and M6e 
            /// </summary>
            NA4 = 33,
            /// <summary>
            /// OPEN region with extended frequency range 840-960MHz for M6ePlus module
            /// </summary>
            OPEN_EXTENDED = 254,
            /// <summary>
            ///Unrestricted access to full hardware range  
            /// </summary>
            OPEN = 0xFF,
        }

        #endregion

        #region SerialTagProtocol

        /// <summary>
        /// Tag Protocol identifiers, as used in Get and Set Current Tag Protocol
        /// </summary>
        public enum SerialTagProtocol
        {
            /// <summary>
            /// No protocol selected
            /// </summary>
            NONE = 0,
            /// <summary>
            /// EPC0 and Matrics-style EPC0+
            /// </summary>
            EPC0_MATRICS = 1,
            /// <summary>
            /// EPC1
            /// </summary>
            EPC1 = 2,
            /// <summary>
            /// ISO 18000-6B
            /// </summary>
            ISO18000_6B = 3,
            /// <summary>
            /// Impinj-style EPC0+
            /// </summary>
            EPC0_IMPINJ = 4,
            /// <summary>
            /// Gen2
            /// </summary>
            GEN2 = 5,
            /// <summary>
            /// UCODE
            /// </summary>
            UCODE = 6,
            /// <summary>
            /// IPX64
            /// </summary>
            IPX64 = 7,
            /// <summary>
            /// IPX256
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
            /// LF125KHZ protocol for the LF reader
            /// </summary>
            LF125KHZ = 0x14,
            /// <summary>
            /// LF134KHZ protocol for the LF reader
            /// </summary>
            LF134KHZ = 0x15,
        }

        #endregion

        #region RegionConfiguration

        /// <summary>
        /// Region-specific parameters that are supported on the device.
        /// </summary>
        public enum RegionConfiguration
        {
            /// <summary>
            /// (bool) Whether LBT is enabled.
            /// </summary>
            LBTENABLED = 0x40,
            /// <summary>
            /// (Int16) LBT Threshold value
            /// </summary>
            LBTTHRESHOLD = 0x41,
            /// <summary>
            /// (bool) Whether Dwell is enabled.
            /// </summary>
            DWELLENABLE = 0x42,
            /// <summary>
            /// (Int16) LBT Dwelltime value
            /// </summary>
            DWELLTIME = 0x43
        }
        #endregion

        #region UserMode

        /// <summary>
        /// enum to define different user modes.
        /// </summary>
        public enum UserMode
        {
            /// <summary>
            /// Default User Mode
            /// </summary>
            NONE = 0x00,
            /// <summary>
            /// Printer Mode
            /// </summary>
            PRINTER = 0x01,
            /// <summary>
            /// Portal Mode
            /// </summary>
            PORTAL = 0x03,
        }
        #endregion

        #region SetUserProfileOption

        /// <summary>
        /// Operation Options for CmdSetUserProfile 
        /// </summary>
        public enum UserConfigOperation
        {
            /// <summary>
            /// Save operation
            /// </summary>
            SAVE = 0x01,

            /// <summary>
            /// Restore operation
            /// </summary>
            RESTORE = 0x02,

            /// <summary>
            /// Verify operation
            /// </summary>
            VERIFY = 0x03,

            /// <summary>
            /// Clear operation
            /// </summary>
            CLEAR = 0x04,

            /// <summary>
            /// Save ReadPlan
            /// </summary>
            SAVEWITHREADPLAN = 0x05,
        }

        #endregion

        #region UserConfigCategory

        /// <summary>
        /// Congfig key for CmdSetUserProfile
        /// </summary>
        public enum UserConfigCategory
        {
            /// <summary>
            /// All configurations
            /// </summary>
            ALL = 0x01,

        }

        #endregion

        #region UserConfigType
        /// <summary>
        /// The config values for CmdSetUserProfile
        /// </summary>
        public enum UserConfigType
        {
            /// <summary>
            /// Firmware default configurations
            /// </summary>
            FIRMWARE_DEFAULT = 0x00,
            /// <summary>
            /// Custom configurations
            /// </summary>
            CUSTOM_CONFIGURATION = 0x01,
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Reader Parameter Identifiers
        /// </summary>
        public enum Configuration
        {
            /// <summary>
            /// Key tag buffer records off of antenna ID as well as EPC;
            /// i.e., keep separate records for the same EPC read on different antennas
            ///   0: Disable -- Different antenna overwrites previous record.
            ///   1: Enable -- Different Antenna creates a new record.
            /// </summary>
            UNIQUE_BY_ANTENNA = 0x00,
            /// <summary>
            /// Run transmitter in lower-performance, power-saving mode.
            ///   0: Disable -- Higher transmitter bias for improved reader sensitivity
            ///   1: Enable -- Lower transmitter bias sacrifices sensitivity for power consumption
            /// </summary>
            TRANSMIT_POWER_SAVE = 0x01,
            /// <summary>
            /// Support 496-bit EPCs (vs normal max 96 bits)
            ///   0: Disable (max max EPC length = 96)
            ///   1: Enable 496-bit EPCs
            /// </summary>
            EXTENDED_EPC = 0x02,
            /// <summary>
            /// Configure GPOs to drive antenna switch.
            ///   0: No switch
            ///   1: Switch on GPO1
            ///   2: Switch on GPO2
            ///   3: Switch on GPO1,GPO2
            /// </summary>
            ANTENNA_CONTROL_GPIO = 0x03,
            /// <summary>
            /// Refuse to transmit if antenna is not detected
            /// </summary>
            SAFETY_ANTENNA_CHECK = 0x04,
            /// <summary>
            /// Refuse to transmit if overtemperature condition detected
            /// </summary>
            SAFETY_TEMPERATURE_CHECK = 0x05,
            /// <summary>
            /// If tag read duplicates an existing tag buffer record (key is the same),
            /// update the record's timestamp if incoming read has higher RSSI reading.
            ///   0: Keep timestamp of record's first read
            ///   1: Keep timestamp of read with highest RSSI
            /// </summary>F
            RECORD_HIGHEST_RSSI = 0x06,
            /// <summary>
            /// Key tag buffer records off tag data as well as EPC;
            /// i.e., keep separate records for the same EPC read with different data
            ///   0: Disable -- Different data overwrites previous record.
            ///   1: Enable -- Different data creates new record.
            /// </summary>
            UNIQUE_BY_DATA = 0x08,
            /// <summary>
            /// Whether RSSI values are reported in dBm, as opposed to
            /// arbitrary uncalibrated units.
            /// </summary>
            RSSI_IN_DBM = 0x09,
            /// <summary>
            /// Self jammer cancellation
            /// User can enable/disable through level2 API
            /// </summary>
            SELF_JAMMER_CANCELLATION = 0x0A,
            /// <summary>
            /// General category of finished reader into which module is integrated; e.g.,
            ///  0: bare module
            ///  1: In-vehicle Reader (e.g., Tool Link, Vega)
            ///  2: USB Reader
            /// </summary>
            PRODUCT_GROUP_ID = 0x12,
            /// <summary>
            /// Product ID (Group ID 0x0002) Information
            /// 0x0001 :M5e-C USB RFID Reader
            /// 0x0002 :Backback NA attenna
            /// 0x0003 :Backback EU attenna
            /// </summary>
            PRODUCT_ID = 0x13,
            /// <summary>
            /// enable/disable tag filtering
            /// </summary>
            ENABLE_FILTERING = 0x0C,
            /// <summary>
            /// Tag Buffer Entry Timeout
            /// User can set the tag buffer timeout
            /// </summary>
            TAG_BUFFER_ENTRY_TIMEOUT = 0x0d,
            /// <summary>
            /// Whether Reads of the same protocol considered same tag
            /// </summary>
            UNIQUE_BY_PROTOCOL = 0x0B,
            /// <summary>
            /// Transport bus type
            /// </summary>
            CURRENT_MESSAGE_TRANSPORT = 0x0E,
            /// <summary>
            /// Send crc
            /// </summary>
            SEND_CRC = 0x1B,
            /// <summary>
            /// Configure GPI for trigger read
            /// </summary>
            CONFIGURATION_TRIGGER_READ_GPIO = 0x1E,
            /// <summary>
            /// Configuration for Keeping RF On
            /// </summary>
            CONFIGURATION_KEEP_RF_ON = 0x21,

        }

        #endregion

        #region AntennaSelection

        /// <summary>
        /// Options for Read Tag Multiple
        /// </summary>
        public enum AntennaSelection
        {
            /// <summary>
            /// Search on single antenna, using current Set Antenna configuration
            /// </summary>
            CONFIGURED_ANTENNA = 0x0000,
            /// <summary>
            /// Search on both monostatic antennas, starting with Antenna 1
            /// </summary>
            ANTENNA_1_THEN_2 = 0x0001,
            /// <summary>
            /// Search on both monostatic antennas, starting with Antenna 2
            /// </summary>
            ANTENNA_2_THEN_1 = 0x0002,
            /// <summary>
            /// Search using the antenna list set by command (0x91 0x02 TX0 RX0 TX1 RX1 ...)
            /// </summary>
            CONFIGURED_LIST = 0x0003,
            /// <summary>
            /// Embedded Command
            /// </summary>
            READ_MULTIPLE_SEARCH_FLAGS_EMBEDDED_OP = 0x0004,
            /// <summary>
            /// Using Streaming
            /// </summary>
            READ_MULTIPLE_SEARCH_FLAGS_TAG_STREAMING = 0x0008,
            /// <summary>
            /// Large Tag Population Support
            /// </summary>
            LARGE_TAG_POPULATION_SUPPORT = 0x0010,
            /// <summary>
            /// Status report streaming
            /// </summary>
            READ_MULTIPLE_SEARCH_FLAGS_STATUS_REPORT_STREAMING = 0x0020,
            /// <summary>
            /// Fast search
            /// </summary>
            READ_MULTIPLE_SEARCH_FLAGS_FAST_SEARCH = 0x0080,
            /// <summary>
            /// Stats report streaming
            /// </summary>
            READ_MULTIPLE_SEARCH_FLAGS_STATS_REPORT_STREAMING = 0x0100,
            /// <summary>
            /// Stop on N trigger flag
            /// </summary>
            READ_MULTIPLE_RETURN_ON_N_TAGS = 0x0040,
            /// <summary>
            /// Read Multiple Gpi Trigger Read
            /// </summary>
            READ_MULTIPLE_SEARCH_FLAG_GPI_TRIGGER_READ = 0x0200,
            /// <summary>
            /// Read Multiple Duty Cycle Control Flag
            /// </summary>
            READ_MULTIPLE_SEARCH_DUTY_CYCLE_CONTROL = 0x0400,
            /// <summary>
            /// Flag indicating dynamic protocol search status
            /// </summary>
            READ_MULTIPLE_SEARCH_DYNAMIC_PROTOCOL_SWITCHING = 0x0800,
        }

        #endregion

        #region ReaderStatisticsFlag

        /// <summary>
        /// Reader Statistics Flag enum
        /// </summary>
        [Flags]
        public enum ReaderStatisticsFlag
        {
            /// <summary>
            /// Total time the port has been transmitting, in milliseconds. Resettable
            /// </summary>
            RF_ON_TIME = 0x01,
            /// <summary>
            /// Detected noise floor with transmitter off. Recomputed when requested, not resettable.  
            /// </summary>
            NOISE_FLOOR = 0x02,
            /// <summary>
            /// Detected noise floor with transmitter on. Recomputed when requested, not resettable.  
            /// </summary>
            NOISE_FLOOR_TX_ON = 0x08,
        }

        #endregion

        #region TagMetadataFlag

        /// <summary>
        /// Enum to define the Tag Metadata.
        /// </summary>
        [Flags]
        public enum TagMetadataFlag
        {
            /// <summary>
            /// No Metadata enabled
            /// </summary>
            NONE = 0x0000,
            /// <summary>
            /// Get read count in Metadata
            /// </summary>
            READCOUNT = 0x0001,
            /// <summary>
            /// Get RSSI count in Metadata
            /// </summary>
            RSSI = 0x0002,
            /// <summary>
            /// Get Antenna ID count in Metadata
            /// </summary>
            ANTENNAID = 0x0004,
            /// <summary>
            /// Get frequency in Metadata
            /// </summary>
            FREQUENCY = 0x0008,
            /// <summary>
            /// Get timestamp in Metadata
            /// </summary>
            TIMESTAMP = 0x0010,
            /// <summary>
            /// Get pahse in Metadata
            /// </summary>
            PHASE = 0x0020,
            /// <summary>
            /// Get protocol in Metadata
            /// </summary>
            PROTOCOL = 0x0040,
            /// <summary>
            /// Get read data in Metadata
            /// </summary>
            DATA = 0x0080,
            ///<summary>
            /// Get GPIO value in Metadata
            /// </summary>
            GPIO = 0x0100,
            ///<summary>
            /// Get Gen2 Q value in Metadata
            /// </summary>
            GEN2_Q = 0x0200,
            ///<summary>
            /// Get Gen2 Link Frequency value in Metadata
            /// </summary>
            GEN2_LF = 0x0400,
            ///<summary>
            /// Get Gen2 Target value in Metadata
            /// </summary>
            GEN2_TARGET = 0x0800,
            ///<summary>
            /// Get Brand Identifier value in Metadata
            /// </summary>
            BRAND_IDENTIFIER = 0x1000,
            ///<summary>
            /// Get Tag Type value in Metadata
            /// </summary>
            TAGTYPE = 0x2000,
            ///<summary>
            /// Shortcut to get all metadata attributes available during normal search.
            /// Excludes data, which requires special operation above and beyond ReadTagMultiple.
            /// </summary>
            ALL = READCOUNT | RSSI | ANTENNAID | FREQUENCY | TIMESTAMP | PROTOCOL | GPIO | PHASE | DATA | GEN2_Q | GEN2_LF | GEN2_TARGET,
        }

        #endregion

        /* for search multiple tag protocol*/

        #region CmdOpcode
        /// <summary>
        /// command opcode for multi-protocol search
        /// </summary>
        public enum CmdOpcode
        {
            /// <summary>
            /// Reserved for future use
            /// </summary>
            RFU = 0x00,
            /// <summary>
            /// Tag read single
            /// </summary>
            TAG_READ_SINGLE = 0x21,
            /// <summary>
            /// tag read multiple
            /// </summary>
            TAG_READ_MULTIPLE = 0x22,
        }

        #endregion

        #region ProtocolBitMask
        /// <summary>
        /// protocol bitmask
        /// </summary>
        public enum ProtocolBitMask
        {
            /// <summary>
            /// protocol bitmask for ISO18000_6B
            /// </summary>
            ISO18000_6B = 0x00000004,
            /// <summary>
            /// protocol bitmask for Gen2
            /// </summary>
            GEN2 = 0x00000010,
            /// <summary>
            /// protocol bitmask for IPX64
            /// </summary>
            IPX64 = 0x00000040,
            /// <summary>
            /// protocol bitmask FOR ipx256
            /// </summary>
            IPX256 = 0x00000080,
            /// <summary>
            /// protocol bitmask for ATA
            /// </summary>
            ATA = 0x00000100,
            /// <summary>
            /// protocol bitmask for all supported protocol
            /// </summary>
            ALL = ISO18000_6B | GEN2 | IPX64 | IPX256 | ATA,
        }

        #endregion

        #region TransportType
        /// <summary>
        /// Transport type
        /// </summary>
        private enum TransportType
        {
            /// <summary>
            /// Serial
            /// </summary>
            SOURCESERIAL = 0x0000,
            /// <summary>
            /// USB
            /// </summary>
            SOURCEUSB = 0x0003,
            /// <summary>
            /// UnKnown
            /// </summary>
            SOURCEUNKNOWN = 0x0004,
        }

        #endregion

        #endregion

        #region Nested Interfaces

        #region ProtocolConfiguration

        /// <summary>
        /// Interface to define Protocol Configurations.
        /// </summary>
        public interface ProtocolConfiguration
        {
            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>Byte value to use in serial protocol field</returns>
            byte GetValue();
        }

        #endregion

        #endregion

        #region Nested Classes

        #region UserConfiguration
        /// <summary>
        /// class for UserConfigOp
        /// </summary>
        public class UserConfigOp
        {
            UserConfigOperation opcode;

            /// <summary>
            /// Set/get UserConfigOperation
            /// </summary>
            public UserConfigOperation Opcode
            {
                get { return opcode; }
                set { opcode = value; }
            }
            // Room for future expansion
            // e.g., UserConfigSection

            /// <summary>
            /// Constructor for UserConfigOp
            /// </summary>
            /// <param name="opcode">Opcode</param>
            public UserConfigOp(UserConfigOperation opcode)
            {
                Opcode = opcode;
            }
        }

        #endregion

        #region AntMuxGpos

        private sealed class AntMuxGpos
        {
            #region Fields

            public byte ConfigByte;
            public int[] GpoList;

            #endregion

            #region Construction

            public AntMuxGpos(byte configByte, int[] gpoList)
            {
                ConfigByte = configByte;
                GpoList = gpoList;
            }

            #endregion
        }

        #endregion

        #region PortParamSetting

        /// <summary>
        ///  Setting for dealing with one of the antenna port params (read power, write power, settling time)
        /// </summary>
        private sealed class PortParamSetting : Setting
        {
            #region Constants

            /// <summary>
            /// Port number column index
            /// </summary>
            public const int PORT = 0;

            /// <summary>
            /// Read power column index
            /// </summary>
            public const int READPOWER = 1;

            /// <summary>
            /// Write power column index
            /// </summary>
            public const int WRITEPOWER = 2;

            /// <summary>
            /// Settling time column index
            /// </summary>
            public const int SETTLINGTIME = 3;

            #endregion

            #region Fields

            /// <summary>
            /// SerialReader to operate on
            /// </summary>
            private SerialReader _serialReader;

            /// <summary>
            /// Index of response column to extract
            /// </summary>
            private int _column;

            #endregion

            #region Construction

            /// <summary>
            /// Create antenna port param Setting object
            /// </summary>
            /// <param name="reader">SerialReader object to operate on</param>
            /// <param name="name">Name of param; e.g., /reader/radio/portReadPowerList</param>
            /// <param name="column">Index of column that houses parameter value
            /// within CmdGetAntennaPortPowersAndSettlingTime response: (port,readpwr,writepwr,settletime).
            /// e.g., 0-port, 1-readpwr, 2-writepwr, 3-settletime</param>
            public PortParamSetting(SerialReader reader, string name, int column)
                : base(name, typeof(int[][]), null, true, null, null, false)
            {
                _serialReader = reader;
                _column = column;

                GetFilter = delegate(Object val)
                {
                    Int16[][] pp = _serialReader.CmdGetAntennaPortPowersAndSettlingTime(column);
                    if (reader.model.Equals("M6e Micro") || reader.model.Equals("M6e Micro USB")
                        || reader.model.Equals("M6e Micro USBPro") || reader.model.Equals("M6e Nano"))
                    {
                        //Set omitzeroes equals to false. Because M6eMicro has power range from -10dBm to +30dBm
                        int[][] values = GetPPColumn(pp, _column, false);
                        List<Int32[]> pvals = new List<Int32[]>();
                        foreach (Int32[] row in values)
                        {
                            if (-32768 != row[1])
                                pvals.Add(new Int32[] { row[0], row[1] });
                        }
                        return pvals.ToArray();
                    }
                    else
                    {
                        return GetPPColumn(pp, _column, true);
                    }
                };

                SetFilter = delegate(Object val)
                {
                    Int16[][] pp = _serialReader.CmdGetAntennaPortPowersAndSettlingTime(column);

                    List<Int16[]> pvalsList = new List<Int16[]>();
                    int[][] values = (int[][])val;
                    foreach (Int32[] row in values)
                    {
                        // fetch the txport mapped to the antenna number passed in values array and replace antenna number with this txport map value.
                        int[] txrx = _txRxMap.GetTxRx(row[0]);
                        pvalsList.Add(new Int16[] { Convert.ToInt16(txrx[0]), Convert.ToInt16(row[1]) });
                    }
                    Int16[][] pvals = pvalsList.ToArray();

                    //foreach (int[] i in pvals)
                    //{
                    //    switch (_column)
                    //    {
                    //        case PortParamSetting.READPOWER:
                    //            {
                    //                if (i[1] < 500)
                    //                {
                    //                    throw new ArgumentOutOfRangeException("Port Read Power can NOT be smaller than 500");
                    //                }
                    //                break;
                    //            }
                    //        case PortParamSetting.WRITEPOWER:
                    //            {
                    //                if (i[1] < 500)
                    //                {
                    //                    throw new ArgumentOutOfRangeException("Port Write Power can NOT be smaller than 500");
                    //                }
                    //                break;
                    //            }
                    //        case PortParamSetting.SETTLINGTIME:
                    //            {
                    //                if (i[1] < 0)
                    //                {
                    //                    throw new ArgumentOutOfRangeException("Port settling time can NOT be negative");
                    //                }
                    //                break;
                    //            }
                    //        default:
                    //            break;

                    //    }
                    //}

                    ModPPColumn(pp, _column, pvals);
                    _serialReader.CmdSetAntennaPortPowersAndSettlingTime(pp, column);
                    return null;
                };
            }

            #endregion

            #region GetPPColumn

            /// <summary>
            /// Extract column from port params response
            /// </summary>
            /// <param name="pp">Response of CmdGetAntennaPortPowersAndSettlingTime (Array of [port,rpwr,wpwr,settletime])</param>
            /// <param name="column">Index of column of interest</param>
            /// <param name="omitZeroes">If true, omit value==0 entries from return</param>
            /// <returns>Array of [port,value] pairs, where value comes from the indicated column.</returns>
            public static Int32[][] GetPPColumn(Int16[][] pp, int column, bool omitZeroes)
            {
                List<Int32[]> pvals = new List<Int32[]>();
                if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA))
                {
                    foreach (Int16[] row in pp)
                    {
                        if ((false == omitZeroes) || (0 != row[1]))
                            pvals.Add(new Int32[] { row[PORT], row[1] });
                    }
                }
                else
                {
                    foreach (Int16[] row in pp)
                    {
                        if ((false == omitZeroes) || (0 != row[column]))
                            pvals.Add(new Int32[] { row[PORT], row[column] });
                    }
                }
                return pvals.ToArray();
            }

            ///// <summary>
            ///// Extract column from port params response
            ///// </summary>
            ///// <param name="pp">Response of CmdGetAntennaPortPowersAndSettlingTime (Array of [port,rpwr,wpwr,settletime])</param>
            ///// <param name="column">Index of column of interest</param>
            ///// <returns>Array of [port,value] pairs, where value comes from the indicated column.
            ///// If value==0, then its row is not included in the return array.</returns>
            //public static Int32[][] GetPPColumn(Int16[][] pp, int column)
            //{                
            //    return GetPPColumn(pp, column, true);
            //}

            #endregion

            #region ModPPColumn

            /// <summary>
            /// Insert column values into port params
            /// </summary>
            /// <param name="pp">Input to CmdSetAntennaPortPowersAndSettlingTime (Array of [port,rpwr,wpwr,settletime])</param>
            /// <param name="column">Index of column of interest</param>
            /// <param name="pvals">Array of [port,value] pairs, where value to be written to the indicated column.</param>
            /// <param name="zeroOmits">If true, zero out pp value if port is not present in pvals array.</param>
            public static void ModPPColumn(Int16[][] pp, int column, Int16[][] pvals, bool zeroOmits)
            {
                if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA))
                {
                    for (int i = 0; i < pp.Length; i++)
                    {
                        Int16 dstport = pp[i][PORT];
                        Int16[] srcrow = FindPortRow(pvals, dstport);

                        if (null == srcrow)
                        {
                            if (zeroOmits)
                                pp[i][1] = 0;
                        }
                        else
                            pp[i][1] = (Int16)srcrow[1];
                    }
                }
                else
                {
                    for (int i = 0; i < pp.Length; i++)
                    {
                        Int16 dstport = pp[i][PORT];
                        Int16[] srcrow = FindPortRow(pvals, dstport);

                        if (null == srcrow)
                        {
                            if (zeroOmits)
                                pp[i][column] = 0;
                        }
                        else
                            pp[i][column] = (Int16)srcrow[1];
                    }
                }

            }

            /// <summary>
            /// Insert column values into port params
            /// </summary>
            /// <param name="pp">Input to CmdSetAntennaPortPowersAndSettlingTime (Array of [port,rpwr,wpwr,settletime])</param>
            /// <param name="column">Index of column of interest</param>
            /// <param name="pvals">Array of [port,value] pairs, where value to be written to the indicated column.</param>
            public static void ModPPColumn(Int16[][] pp, int column, Int16[][] pvals)
            {
                //Set to false, don't zero out pp value if port is not present in pvals array as this also makes the power of different ports to zero.
                ModPPColumn(pp, column, pvals, false);
            }

            #endregion

            #region FindPortRow

            /// <summary>
            /// Find row with corresponding port number
            /// </summary>
            /// <param name="pvals">Array of [port,val,...]</param>
            /// <param name="port">Port number to search for</param>
            /// <returns>[port,val,...] row that Matches requested port, or null if not found.</returns>
            private static Int16[] FindPortRow(Int16[][] pvals, int port)
            {
                foreach (Int16[] row in pvals)
                {
                    if (port == row[0])
                        return row;
                }

                return null;
            }

            #endregion
        }

        #endregion

        #region TxRxMap

        /// <summary>
        /// Mapping of virtual antenna numbers to (tx,rx) pairs.
        /// Supports several types of lookup:
        ///  * virtant -> (tx,rx)
        ///  * (tx,rx) -> virtant
        ///  * tx -> virtant
        ///    * (Used for automagic selection of connected antennas.  Given connectedPortList, create a list of monostatic antennas (assumes all TX ports can run monostatic).
        /// </summary>
        private sealed class TxRxMap
        {
            #region Fields

            SerialReader _rdr;
            private int[][] _rawTuples;  // (virt,tx,rx)
            private Dictionary<int, int[]> _forwardMap;  // virt -> (tx,rx)
            private Dictionary<byte, int> _reverseMap;  // M5e Get Tag Buffer format: (tx<<4)|rx -> virt 

            #endregion

            #region Properties

            /// <summary>
            /// Raw (virtant,txport,rxport) tuples
            /// </summary>
            public int[][] Map
            {
                get { return (int[][])_rawTuples.Clone(); }
                // TODO: What do we need to invalidate when TxRxMap changes?  tagopantenna?  searchlist?  Anything that deals with antenna numbers.
                set
                {
                    // Don't need to clone value.  We're not saving a reference,
                    // just copying values out of it.
                    int[][] newRawTuples = value;

                    Dictionary<int, int> ttxxrrxx = new Dictionary<int, int>();
                    Dictionary<int, int[]> newForwardMap = new Dictionary<int, int[]>();
                    Dictionary<byte, int> newReverseMap = new Dictionary<byte, int>();

                    foreach (int[] tuple in newRawTuples)
                    {
                        int virt = tuple[0];
                        byte tx = (byte)tuple[1];
                        byte rx = (byte)tuple[2];

                        ValidateParameter<int>(tx, _rdr.PortList, "Invalid antenna port");
                        ValidateParameter<int>(rx, _rdr.PortList, "Invalid antenna port");


                        ttxxrrxx.Add(tx, rx);
                        // ForwardMap converts API antenna into module Set Antenna arguments
                        int[] txrx = new int[] { tx, rx };

                        //ttxxrrxx.Add(tx, rx);
                        newForwardMap.Add(virt, txrx);

                        // ReverseMap converts module Tag Buffer fields into API antenna
                        // Serial protocol's TagReadData format restricts tx and rx to 4 bits
                        // Do same here to properly wraparound antenna 16 to 0
                        byte txrxbyte = (byte)(((tx & 0xF) << 4) | (rx & 0xF));

                        if ((_rdr.portmask & 0x04) != 0x00)
                        {
                            if (!newReverseMap.ContainsKey(txrxbyte))
                                newReverseMap.Add(txrxbyte, virt);
                        }
                        else
                            if (!newReverseMap.ContainsKey(txrxbyte))
                                newReverseMap.Add(txrxbyte, virt);
                    }

                    // Update state only after all validation succeeds
                    _rawTuples = newRawTuples;
                    _forwardMap = newForwardMap;
                    _reverseMap = newReverseMap;
                }
            }
            /// <summary>
            /// List of valid logical antenna numbers
            /// </summary>
            public int[] ValidAntennas
            {
                get
                {
                    List<int> ants = new List<int>();

                    foreach (int[] row in _rawTuples)
                        ants.Add(row[0]);

                    return ants.ToArray();
                }
            }

            #endregion

            #region Construction

            /// <summary>
            /// Create TxRxMap from list of antenna ports: all monostatic, virtual antenna matches port number.
            /// e.g., default TxRxMap is
            ///   for each port in [list of available physical ports]
            ///     (logical_antenna=port, txport=port, rxport=port)
            /// </summary>
            /// <param name="antennaPorts">List of antenna ports</param>
            /// <param name="reader">Parent reader</param>
            public TxRxMap(int[] antennaPorts, SerialReader reader)
            {
                _rdr = reader;
                List<int[]> map = new List<int[]>();


                foreach (int port in antennaPorts)
                {
                    map.Add(new int[] { port, port, port });
                }
                Map = map.ToArray();
            }

            #endregion

            #region GetTxRx

            /// <summary>
            /// Map virtual antenna  to (tx,rx) ports
            /// </summary>
            /// <param name="virt">Virtual antenna number</param>
            /// <returns>(txport,rxport)</returns>
            public int[] GetTxRx(int virt)
            {
                if (!_forwardMap.ContainsKey(virt))
                    throw new ArgumentException("Invalid logical antenna number: " + virt.ToString());

                return _forwardMap[virt];
            }

            #endregion

            #region GetDefaultAntenna

            /// <summary>
            /// Map virtual antenna  to (tx,rx) ports
            /// </summary>
            /// <param name="virt">Virtual antenna number</param>
            /// <returns>(txport,rxport)</returns>
            public int GetDefaultAntenna(int[] virt)
            {
                int pivot = 0;
                foreach (KeyValuePair<int, int[]> val in _forwardMap)
                {
                    int[] temp = val.Value;
                    if (temp[0] == virt[0] && temp[1] == virt[1])
                    {
                        pivot = (int)val.Key;
                    }
                }
                return pivot;
                //return _forwardMap[virt];
            }

            #endregion


            #region GetVirt

            /// <summary>
            /// Map (tx,rx) ports to virtual antenna
            /// </summary>
            /// <param name="txrx">(txport,rxport)</param>
            /// <returns>virtual antenna number</returns>
            public int GetVirt(byte txrx)
            {
                if (!_reverseMap.ContainsKey(txrx))
                {
                    throw new ArgumentException(String.Format(
                        "No logical antenna mapping: txrx({0:D},{1:D})",
                        (txrx >> 4) & 0xF, (txrx >> 0) & 0xF));
                }
                return _reverseMap[txrx];
            }

            #endregion


            #region TranslateSerialAntenna

            /// <summary>
            /// Translate antenna ID from M5e Get Tag Buffer command to logical antenna number
            /// </summary>
            /// <param name="serant">M5e serial protocol antenna ID (TX: 4 msbs, RX: 4 lsbs)</param>
            /// <returns>Logical antenna number</returns>
            public int TranslateSerialAntenna(byte serant)
            {
                try
                {
                    return GetVirt(serant);
                }
                catch (ArgumentException)
                {
                    int tx = (serant >> 4) & 0xF;
                    int rx = (serant >> 0) & 0xF;
                    throw new ReaderParseException(String.Format(
                        "Can't find mapping from txrx({0:D},{1:D}) to logical antenna number",
                        tx, rx));
                }
            }

            #endregion
        }

        #endregion

        #region VersionInfo

        /// <summary>
        /// Parsed response to Get Version
        /// </summary>
        public sealed class VersionInfo
        {
            /// <summary>
            /// Bootloader version
            /// </summary>
            public VersionNumber Bootloader;

            /// <summary>
            /// Hardware version
            /// </summary>
            public VersionNumber Hardware;

            /// <summary>
            /// Firmware version
            /// </summary>
            public VersionNumber Firmware;

            /// <summary>
            /// Firmware timestamp
            /// </summary>
            public VersionNumber FirmwareDate;

            /// <summary>
            /// List of supported protocols
            /// </summary>
            public TagProtocol[] SupportedProtocols;
        }

        #endregion

        #region AntennaPort

        /// <summary>
        /// Object representing state of reader's antennas
        /// </summary>
        public sealed class AntennaPort
        {
            /// <summary>
            /// The current logical transmit antenna port.
            /// </summary>
            public int TxAntenna;
            /// <summary>
            /// The current logical receive antenna port.
            /// </summary>
            public int RxAntenna;
            /// <summary>
            /// List of physical antenna ports
            /// </summary>
            public int[] PortList;
            /// <summary>
            /// List of physical antenna ports where an antenna has been detected.
            /// </summary>
            public int[] TerminatedPortList;
        }

        #endregion

        #region HibikiSystemInformation

        /// <summary>
        /// Hibiki System Information.
        /// </summary>
        public sealed class HibikiSystemInformation
        {
            /// <summary>
            /// Indicates whether the banks are present and Custom Commands are implemented 
            /// </summary>
            public UInt16 infoFlags;
            /// <summary>
            /// Indicates the size of this memory bank in words
            /// </summary>
            public byte reservedMemory;
            /// <summary>
            /// Indicates the size of this memory bank in words
            /// </summary>
            public byte epcMemory;
            /// <summary>
            /// Indicates the size of this memory bank in words
            /// </summary>
            public byte tidMemory;
            /// <summary>
            /// Indicates the size of this memory bank in words
            /// </summary>
            public byte userMemory;
            /// <summary>
            /// Indicates state of Attenuation
            /// </summary>
            public byte setAttenuate;
            /// <summary>
            /// Indicates Lock state for Bank Lock
            /// </summary>
            public UInt16 bankLock;
            /// <summary>
            /// Indicates Lock state for Block Read Lock
            /// </summary>
            public UInt16 blockReadLock;
            /// <summary>
            /// Indicates Lock state for Block ReadWrite Lock
            /// </summary>
            public UInt16 blockRwLock;
            /// <summary>
            /// Indicates Lock state for Block Write Lock
            /// </summary>
            public UInt16 blockWriteLock;
        }
        #endregion

        #region ReaderStatistics

        /// <summary>
        /// Reader Statistics class.
        /// </summary>
        public sealed class ReaderStatistics
        {
            /// <summary>
            /// Number of ports.
            /// </summary>
            public int numPorts;
            /// <summary>
            /// Per-Port RF on time, in milliseconds
            /// </summary>
            public UInt32[] rfOnTime;
            /// <summary>
            /// Per-Port Noise Floor
            /// </summary>
            public byte[] noiseFloor;
            /// <summary>
            /// Per-Port noise floor while transmitting
            /// </summary>
            public byte[] noiseFloorTxOn;

            #region ToString
            /// <summary>
            /// Human-readable representation
            /// </summary>
            /// <returns>Human-readable representation</returns>
            public override string ToString()
            {
                string rfontime = string.Empty, comma = string.Empty;
                foreach (UInt32 i in rfOnTime) { rfontime += comma + i.ToString(); comma = ","; }
                string strNoiseFloorTxOn = string.Empty;
                comma = string.Empty;
                foreach (byte b in noiseFloorTxOn) { strNoiseFloorTxOn += comma + b.ToString(); comma = ","; }
                string strNoiseFloor = string.Empty;
                comma = string.Empty;
                foreach (byte b in noiseFloor) { strNoiseFloor += comma + b.ToString(); comma = ","; }

                return "Number of ports:" + numPorts.ToString() + ", Per-Port RF on time:" + rfontime + ", Per-Port noise floor :" + strNoiseFloor + ", Per-Port noise floor while transmitting:" + strNoiseFloorTxOn;
            }
            #endregion ToString
        }
        #endregion

        #region Gen2Configuration

        /// <summary>
        /// Option key value for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Gen2Configuration : ProtocolConfiguration
        {
            #region Static Fields

            private static Gen2Configuration _session = new Gen2Configuration(0x00);
            private static Gen2Configuration _target = new Gen2Configuration(0x01);
            private static Gen2Configuration _tagEncoding = new Gen2Configuration(0x02);
            private static Gen2Configuration _linkFrequency = new Gen2Configuration(0x10);
            private static Gen2Configuration _tari = new Gen2Configuration(0x11);
            private static Gen2Configuration _q = new Gen2Configuration(0x12);
            private static Gen2Configuration _bap = new Gen2Configuration(0x13);
            private static Gen2Configuration _protocolExtension = new Gen2Configuration(0x14);
            private static Gen2Configuration _t4 = new Gen2Configuration(0x15);
            private static Gen2Configuration _initQ = new Gen2Configuration(0x16);
            private static Gen2Configuration _sendSelect = new Gen2Configuration(0x17);

            #endregion

            #region Fields

            private byte value;

            #endregion

            #region Static Properties

            /// <summary>
            /// Gen2 Session
            /// </summary>
            public static Gen2Configuration SESSION { get { return _session; } }

            /// <summary>
            /// Gen2 Target
            /// </summary>           
            public static Gen2Configuration TARGET { get { return _target; } }

            /// <summary>
            /// Gen2 Miller cycles
            /// </summary>
            public static Gen2Configuration TAGENCODING { get { return _tagEncoding; } }

            /// <summary>
            /// Gen2 Q
            /// </summary>
            public static Gen2Configuration Q { get { return _q; } }

            /// <summary>
            /// Gen2 Link Frequency
            /// </summary>
            public static Gen2Configuration LINKFREQUENCY { get { return _linkFrequency; } }

            /// <summary>
            /// Gen2 TARI
            /// </summary>
            public static Gen2Configuration TARI { get { return _tari; } }

            /// <summary>
            /// Gen2 BAP
            /// </summary>
            public static Gen2Configuration BAP { get { return _bap; } }

            /// <summary>
            /// Gen2 Protocol Extension
            /// </summary>
            public static Gen2Configuration PROTOCOLEXTENSION { get { return _protocolExtension; } }

            /// <summary>
            /// Gen2 T4
            /// </summary>
            public static Gen2Configuration T4 { get { return _t4; } }

            /// <summary>
            /// InitQ
            /// </summary>
            public static Gen2Configuration INITQ { get { return _initQ; } }

            /// <summary>
            /// SendSelect
            /// </summary>
            public static Gen2Configuration SENDSELECT { get { return _sendSelect; } }


            #endregion

            #region Construction

            Gen2Configuration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>Byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region Iso180006bConfiguration

        /// <summary>
        /// Option key values for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Iso180006bConfiguration : ProtocolConfiguration
        {
            #region Static Fields

            private static Iso180006bConfiguration _linkfrequency = new Iso180006bConfiguration(0x10);
            private static Iso180006bConfiguration modulationDepth = new Iso180006bConfiguration(0x11);
            private static Iso180006bConfiguration delimiter = new Iso180006bConfiguration(0x12);

            #endregion

            #region Static Properties

            /// <summary>
            /// ISO Link Frequency.
            /// </summary>
            public static Iso180006bConfiguration LINKFREQUENCY { get { return _linkfrequency; } }
            /// <summary>
            /// ISO Modulation Depth
            /// </summary>
            public static Iso180006bConfiguration MODULATIONDEPTH { get { return modulationDepth; } }
            /// <summary>
            /// ISO Delimiter
            /// </summary>
            public static Iso180006bConfiguration DELIMITER { get { return delimiter; } }
            private byte value;

            #endregion

            #region Construction

            Iso180006bConfiguration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>Byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region Iso14443aConfiguration

        /// <summary>
        /// Option key values for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Iso14443aConfiguration : ProtocolConfiguration
        {
            #region Static Fields

            private static Iso14443aConfiguration _supportedTagTypes = new Iso14443aConfiguration(0x01);
            private static Iso14443aConfiguration _tagType = new Iso14443aConfiguration(0x02);
            private static Iso14443aConfiguration _supportedTagFeatures = new Iso14443aConfiguration(0x03);

            #endregion

            #region Static Properties

            /// <summary>
            /// ISO14443A Supported Tag Types.
            /// </summary>
            public static Iso14443aConfiguration SUPPORTEDTAGTYPES { get { return _supportedTagTypes; } }
            /// <summary>
            /// ISO14443A Tag Type.
            /// </summary>
            public static Iso14443aConfiguration TAGTYPE { get { return _tagType; } }
            /// <summary>
            /// ISO14443A Supported Tag features
            /// </summary>
            public static Iso14443aConfiguration SUPPORTEDTAGFEATURES { get { return _supportedTagFeatures; } }
            private byte value;

            #endregion

            #region Construction

            Iso14443aConfiguration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region Iso14443bConfiguration

        /// <summary>
        /// Option key values for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Iso14443bConfiguration : ProtocolConfiguration
        {
            #region Static Fields

            private static Iso14443bConfiguration _supportedTagTypes = new Iso14443bConfiguration(0x01);
            private static Iso14443bConfiguration _tagType = new Iso14443bConfiguration(0x02);

            #endregion

            #region Static Properties

            /// <summary>
            /// ISO14443B Supported Tag Types.
            /// </summary>
            public static Iso14443bConfiguration SUPPORTEDTAGTYPES { get { return _supportedTagTypes; } }
            /// <summary>
            /// ISO14443B Tag Type.
            /// </summary>
            public static Iso14443bConfiguration TAGTYPE { get { return _tagType; } }
            private byte value;

            #endregion

            #region Construction

            Iso14443bConfiguration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region Iso15693Configuration
        /// <summary>
        /// Option key values for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Iso15693Configuration : ProtocolConfiguration
        {
            #region Static Fields

            private static Iso15693Configuration _supportedTagType = new Iso15693Configuration(0x01);
            private static Iso15693Configuration _tagType = new Iso15693Configuration(0x02);
            private static Iso15693Configuration _supportedTagFeatures = new Iso15693Configuration(0x03);

            #endregion

            #region Static Properties

            /// <summary>
            /// ISO15693 Supported Tag Types.
            /// </summary>
            public static Iso15693Configuration SUPPORTEDTAGTYPES { get { return _supportedTagType; } }
            /// <summary>
            /// ISO15693 Tag Type.
            /// </summary>
            public static Iso15693Configuration TAGTYPE { get { return _tagType; } }
            /// <summary>
            /// ISO15693 TagFeatures
            /// </summary>
            public static Iso15693Configuration SUPPORTEDTAGFEATURES { get { return _supportedTagFeatures; } }
            private byte value;

            #endregion

            #region Construction

            Iso15693Configuration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region Lf125khzConfiguration
        /// <summary>
        /// Option key values for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Lf125khzConfiguration : ProtocolConfiguration
        {
            #region Static Fields

            private static Lf125khzConfiguration _supportedTagType = new Lf125khzConfiguration(0x01);
            private static Lf125khzConfiguration _tagType = new Lf125khzConfiguration(0x02);
            private static Lf125khzConfiguration _supportedTagFeatures = new Lf125khzConfiguration(0x03);
            private static Lf125khzConfiguration _secureReadFormat = new Lf125khzConfiguration(0x04);

            #endregion

            #region Static Properties

            /// <summary>
            /// LF125KHZ Supported Tag Types.
            /// </summary>
            public static Lf125khzConfiguration SUPPORTEDTAGTYPES { get { return _supportedTagType; } }
            /// <summary>
            /// LF125KHZ Tag Type.
            /// </summary>
            public static Lf125khzConfiguration TAGTYPE { get { return _tagType; } }
            /// <summary>
            /// LF125KHZ TagFeatures
            /// </summary>
            public static Lf125khzConfiguration SUPPORTEDTAGFEATURES { get { return _supportedTagFeatures; } }
            /// <summary>
            /// LF125KHZ Secure read format
            /// </summary>
            public static Lf125khzConfiguration SECUREREADFORMAT { get { return _secureReadFormat; } }
            private byte value;

            #endregion

            #region Construction

            Lf125khzConfiguration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region Lf134khzConfiguration
        /// <summary>
        /// Option key values for serial Set Protocol Configuration command
        /// </summary>
        public sealed class Lf134khzConfiguration : ProtocolConfiguration
        {
            #region Static Fields

            private static Lf134khzConfiguration _supportedTagType = new Lf134khzConfiguration(0x01);
            private static Lf134khzConfiguration _tagType = new Lf134khzConfiguration(0x02);

            #endregion

            #region Static Properties

            /// <summary>
            /// LF134KHZ Supported Tag Types.
            /// </summary>
            public static Lf134khzConfiguration SUPPORTEDTAGTYPES { get { return _supportedTagType; } }
            /// <summary>
            /// LF134KHZ Tag Type.
            /// </summary>
            public static Lf134khzConfiguration TAGTYPE { get { return _tagType; } }
            private byte value;

            #endregion

            #region Construction

            Lf134khzConfiguration(byte v)
            {
                value = v;
            }

            #endregion

            #region GetValue

            /// <summary>
            ///  Extract serial protocol enum value
            /// </summary>
            /// <returns>byte value to use in serial protocol field</returns>
            public byte GetValue()
            {
                return value;
            }

            #endregion
        }

        #endregion

        #region SingulationBytes

        /// <summary>
        /// Bytes to add to M5e command to enable Tag Singulation
        /// </summary>
        internal class SingulationBytes
        {
            #region Fields

            /// <summary>
            /// Singulation option code, appears as first command argument
            /// </summary>
            public byte Option;

            /// <summary>
            /// Singulation mask specification, appears at end of command arguments
            /// </summary>
            public byte[] Mask;

            #endregion
        }
        #endregion

        #endregion

        #region Static Fields

        private static readonly Dictionary<SerialRegion, Region> _mapM5eToTmRegion = new Dictionary<SerialRegion, Region>();
        private static readonly Dictionary<Region, SerialRegion> _mapTmToM5eRegion = new Dictionary<Region, SerialRegion>();


        private static readonly AntMuxGpos[] antmuxsettings = new AntMuxGpos[] 
        {
            new AntMuxGpos(0x00, new int[]{}),
            new AntMuxGpos(0x01, new int[]{1}), 
            new AntMuxGpos(0x02, new int[]{2}), 
            new AntMuxGpos(0x03, new int[]{1,2}),
            new AntMuxGpos(0x03, new int[]{2,1}),  // Alias for {1,2}
        };
        private static VersionNumber m4eType1 = new VersionNumber(0xFF, 0xFF, 0xFF, 0xFF);
        private static VersionNumber m4eType2 = new VersionNumber(0x01, 0x01, 0x00, 0x00);

        // List of M6e readers
        private static readonly List<string> M6eFamilyList = new List<string>();
        // List of M5e readers
        private static readonly List<string> M5eFamilyList = new List<string>();

        #endregion

        #region Fields

        private int _debug = 0; // Turns on Debug Mode.
        private int _currentAntenna;
        private int[] _searchList;
        private byte[][] _antdetResult = null;
        private List<int> _physPortList = null;  // Physical ports on module
        private List<int> _connectedPhysPortList = null; // Connected physical ports
        private SerialTransport _serialPort = null;
        private string _readerUri;
        private bool shouldDispose = true;
        private string _universalComPort = "COM1";
        private int _universalBaudRate = 115200;
        private int currentBaudRate;
        /* Check if user has set baudrate explicitly */
        private bool isUserBaudRateSet = false;
        private Region universalRegion = Region.UNSPEC;
        private static TxRxMap _txRxMap;
        /// <summary>
        /// Get the model name.
        /// </summary>
        public string model = null;
        private PowerMode powerMode = PowerMode.INVALID;
        private byte opCode; //stores command opCode for comparison
        private bool supportsPreamble = false;
        private List<TagProtocol> supportedProtocols;
        private bool isExtendedEpc = false;
        private bool continuousReadActive = false;
        private bool isTrueContinuousRead = false;
        private bool isReadStarted = false; //becomes true after sending read command to module
        private bool isReadInitiated = false; //becomes true when StartReading() method is called
        private ManualResetEvent waitUntilReadMethodCalled = new ManualResetEvent(false);
        private int tagOpSuccessCount = 0;
        private int tagOpFailuresCount = 0;
        private ManualResetEvent asyncStoppedEvent;
        private bool antennaStatusEnable = false;
        private bool frequencyStatusEnable = false;
        private bool temperatureStatusEnable = false;
        private int statusFlags = 0x00;
        private const int DEFAULT_READ_FILTER_TIMEOUT = -1;
        private bool _enableFiltering = true;
        // To check whether user has changed filter value
        private bool userEnableReadFiltering = false;
        private int _readFilterTimeout = DEFAULT_READ_FILTER_TIMEOUT;
        private bool uniqueByAntenna = false;
        private bool uniqueByData = false;
        private bool uniqueByProtocol = false;
        private bool isRecordHighestRssi = false;
        private bool updateAntennaDetection = true;
        private UInt16 productGroupID = 0;
        private static bool isSecurePasswordLookupEnabled = false;
        private static bool isSecureAccessEnabled = false;
        private static bool isSupportsResetStats = false;
        private bool isGen2AllMemoryBankEnabled = false;
        // Cache number of tags to read
        private uint numberOfTagsToRead = 0;
        // Stop trigger feature enabled or disabled
        private bool isStopNTags = false;
        // Cache the host time
        private DateTime baseTime;
        private DateTime LastTagTime;
        private static TagMetadataFlag metadataFlags = TagMetadataFlag.ALL;
        private static TagMetadataFlag m3eMetadataFlags = TagMetadataFlag.READCOUNT | TagMetadataFlag.ANTENNAID | TagMetadataFlag.TIMESTAMP | TagMetadataFlag.PROTOCOL | TagMetadataFlag.TAGTYPE | TagMetadataFlag.DATA;
        // Indicates the whether model is M3e or not
        private static bool isModelM3e = false;
        // Indicates M3e supported stats
        private static Reader.Stat.StatsFlag m3eStatsFlags = Reader.Stat.StatsFlag.TEMPERATURE | Reader.Stat.StatsFlag.DCVOLTAGE;
        private byte portmask = new byte();
        private bool paramWait = false;
        private List<byte> paramMessage = new List<byte>();
        private bool hasContinuousReadStarted = false;
        private bool hasTagReportCleared = false;

        // Cache value of tagtype here as flags
        private List<byte> tagTypeFlags = new List<byte>();

        /// <summary>
        /// Enable/disable BAP feature
        /// </summary>
        private bool isBAPEnabled = false;
        /// <summary>
        /// Default probe baudrates
        /// </summary>
        int[] probeBaudRates = new int[] { 9600, 115200, 921600, 19200, 38400, 57600, 230400, 460800 };

        /// <summary>
        /// Set the current protocol
        /// </summary>
        private TagProtocol CurrentProtocol
        {
            get { return currentProtocol; }
            set { currentProtocol = value; }
        }
        /// <summary>
        /// The switch to turn on/off the tag read streaming
        /// </summary>
        private bool enableStreaming = false;

        /// <summary>
        /// Metadata flags
        /// </summary>
        public TagMetadataFlag userMeta = TagMetadataFlag.ALL;

        /// <summary>
        /// Reader version information returned by M5e library
        /// </summary>
        /// <remarks>DO NOT use _supportedProtocols directly!  The numbers don't match --
        /// they're in M5e serial protocol format, not ThingMagic.TagProtocol.</remarks>
        private VersionInfo _version = new VersionInfo();

        private TagProtocol currentProtocol;

        /// <summary>
        /// Transport type enum
        /// </summary>
        private TransportType transportType;

        /// <summary>
        /// To enable or disable crc calculations
        /// </summary>
        private bool isCRCEnabled = true;

        /// <summary>
        /// To enable or disable triggerRead
        /// </summary>
        bool isTriggerReadEnable = false;

        /// <summary>
        /// To enable or disable multi filter
        /// </summary>
        static bool isMultiFilterEnabled = false;

        #endregion

        #region Construction

        static SerialReader()
        {
            // Initialize m5e to ThingMagic Region Dictionary
            _mapM5eToTmRegion.Add(SerialRegion.EU, Region.EU);
            _mapM5eToTmRegion.Add(SerialRegion.EU2, Region.EU2);
            _mapM5eToTmRegion.Add(SerialRegion.EU3, Region.EU3);
            _mapM5eToTmRegion.Add(SerialRegion.IN, Region.IN);
            _mapM5eToTmRegion.Add(SerialRegion.JP, Region.JP);
            _mapM5eToTmRegion.Add(SerialRegion.KR, Region.KR);
            _mapM5eToTmRegion.Add(SerialRegion.KR2, Region.KR2);
            _mapM5eToTmRegion.Add(SerialRegion.NA, Region.NA);
            _mapM5eToTmRegion.Add(SerialRegion.OPEN, Region.OPEN);
            _mapM5eToTmRegion.Add(SerialRegion.PRC, Region.PRC);
            _mapM5eToTmRegion.Add(SerialRegion.PRC2, Region.PRC2);
            _mapM5eToTmRegion.Add(SerialRegion.AU, Region.AU);
            _mapM5eToTmRegion.Add(SerialRegion.NZ, Region.NZ);
            _mapM5eToTmRegion.Add(SerialRegion.NA2, Region.NA2);
            _mapM5eToTmRegion.Add(SerialRegion.NA3, Region.NA3);
            _mapM5eToTmRegion.Add(SerialRegion.IS, Region.IS);
            _mapM5eToTmRegion.Add(SerialRegion.MY, Region.MY);
            _mapM5eToTmRegion.Add(SerialRegion.ID, Region.ID);
            _mapM5eToTmRegion.Add(SerialRegion.PH, Region.PH);
            _mapM5eToTmRegion.Add(SerialRegion.TW, Region.TW);
            _mapM5eToTmRegion.Add(SerialRegion.MO, Region.MO);
            _mapM5eToTmRegion.Add(SerialRegion.RU, Region.RU);
            _mapM5eToTmRegion.Add(SerialRegion.SG, Region.SG);
            _mapM5eToTmRegion.Add(SerialRegion.JP2, Region.JP2);
            _mapM5eToTmRegion.Add(SerialRegion.JP3, Region.JP3);
            _mapM5eToTmRegion.Add(SerialRegion.VN, Region.VN);
            _mapM5eToTmRegion.Add(SerialRegion.TH, Region.TH);
            _mapM5eToTmRegion.Add(SerialRegion.AR, Region.AR);
            _mapM5eToTmRegion.Add(SerialRegion.HK, Region.HK);
            _mapM5eToTmRegion.Add(SerialRegion.BD, Region.BD);
            _mapM5eToTmRegion.Add(SerialRegion.EU4, Region.EU4);
            _mapM5eToTmRegion.Add(SerialRegion.UNIVERSAL, Region.UNIVERSAL);
            _mapM5eToTmRegion.Add(SerialRegion.IS2, Region.IS2);
            _mapM5eToTmRegion.Add(SerialRegion.NA4, Region.NA4);
            _mapM5eToTmRegion.Add(SerialRegion.OPEN_EXTENDED, Region.OPEN_EXTENDED);
            _mapM5eToTmRegion.Add(SerialRegion.NONE, Region.UNSPEC);

            // Initialize ThingMagic to m5e Region Dictionary
            foreach (KeyValuePair<SerialRegion, Region> kvp in _mapM5eToTmRegion)
                _mapTmToM5eRegion.Add(kvp.Value, kvp.Key);

            // Initialize M6e family list
            M6eFamilyList.Add("M6e");
            M6eFamilyList.Add("M6e Micro");
            M6eFamilyList.Add("M6e Micro USB");
            M6eFamilyList.Add("M6e Micro USBPro");
            M6eFamilyList.Add("M6e PRC");
            M6eFamilyList.Add("M6e Nano");
            M6eFamilyList.Add("M6e JIC");

            // Initialize M5e family list
            M5eFamilyList.Add("M5e");
            M5eFamilyList.Add("M5e Compact");
            M5eFamilyList.Add("M5e EU");
            M5eFamilyList.Add("M5e NA");
            M5eFamilyList.Add("M5e JP");
            M5eFamilyList.Add("M5e PRC");
        }

        /// <summary>
        /// Make a serial reader with default transport
        /// </summary>
        /// <param name="readerUri">URI-style path to serial device; e.g., /COM1</param>
        public SerialReader(string readerUri)
            : this(readerUri, new SerialTransportNative()) { }

        /// <summary>
        /// Connect to a reader
        /// </summary>
        /// <param name="readerUri">URI-style path to serial device; e.g., /COM1</param>
        /// <param name="transport">Serial transport object</param>
        public SerialReader(string readerUri, SerialTransport transport)
        {
            _readerUri = readerUri;
            _serialPort = transport;

            // Pre-connect params
            ParamAdd(new Setting("/reader/baudRate", typeof(int), _universalBaudRate, true,
                delegate(Object val) { return _universalBaudRate; },
                delegate(Object val)
                {
                    _universalBaudRate = (int)val;
                    isUserBaudRateSet = true;
                    if (_serialPort.IsOpen)
                    {
                        ChangeBaudRate(_universalBaudRate);
                        return _serialPort.BaudRate;
                    }
                    return _universalBaudRate;
                }
                ));

            ParamAdd(new Setting("/reader/probeBaudRates", typeof(int[]), null, true,
                delegate(Object val) { return probeBaudRates; },
                delegate(Object val)
                {
                    int[] baudRates = (int[])val;
                    if (baudRates.Length > 8)
                    {
                        throw new ArgumentException("ProbeBaudrates length shouldn't be greater than 8 ");
                    }
                    probeBaudRates = baudRates;
                    // Array.Copy(baudRates, probeBaudRates, baudRates.Length);
                    return probeBaudRates;
                }
                ));

            ParamAdd(new Setting("/reader/powerMode", typeof(PowerMode), null, true,
                delegate(Object val)
                {
                    if (_serialPort.IsOpen)
                        return CmdGetPowerMode();
                    else
                        return val;
                },
                delegate(Object val)
                {
                    if (_serialPort.IsOpen)
                        CmdSetPowerMode((PowerMode)val);
                    // Don't change internal powerMode before module has changed;
                    // sendMessage needs it to calculate wakeup preamble
                    powerMode = (PowerMode)val;

                    return powerMode;
                },
                false));

            ParamAdd(new Setting("/reader/region/id", typeof(Region), null, true,
                delegate(Object val) { return CmdGetRegion(); },
                delegate(Object val)
                {
                    universalRegion = (Region)val;
                    if (_serialPort.IsOpen)
                    {
                        CmdSetRegion(universalRegion);
                        return val;
                    }
                    return universalRegion;
                }));
        }

        #endregion

        #region Configuration Settings Methods

        #region LoadParams

        private void LoadParams()
        {
            // TODO: Rewrite ParamAdds to take advantage of new features: can omit set/get args, don't have to explicitly write Clone into get filter.

            // Create productGroupID early, so other params (e.g., txRxMap) can use it
            ParamAdd(new Setting("/reader/version/productGroupID", typeof(UInt16), null, false,
                delegate(Object val)
                {
                    try
                    {
                        return CmdGetReaderConfiguration(Configuration.PRODUCT_GROUP_ID);
                    }
                    catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception)
                    {
                        return (UInt16)0xffff;
                    }
                },
                null));
            ParamAdd(new Setting("/reader/version/productID", typeof(UInt16), null, false,
                delegate(Object val)
                {
                    try
                    {
                        return CmdGetReaderConfiguration(Configuration.PRODUCT_ID);
                    }
                    catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception)
                    {
                        return (UInt16)0xffff;
                    }
                },
                null));
            ParamAdd(new Setting("/reader/version/productGroup", typeof(string), null, false,
                delegate(Object val)
                {
                    UInt16 id = (UInt16)ParamGet("/reader/version/productGroupID");
                    switch (id)
                    {
                        case (UInt16)ProductGroupID.MODULE:
                        case (UInt16)ProductGroupID.INVALID:
                            return "Embedded Reader";
                        case (UInt16)ProductGroupID.RUGGEDIZED_READER:
                            return "Ruggedized Reader";
                        case (UInt16)ProductGroupID.USB_READER:
                            return "USB Reader";
                        default:
                            return "Unknown";
                    }
                },
                null));

            ConfigureForProductGroup();
            updateAntennaDetection = false;
            ParamAdd(new Setting("/reader/radio/enablePowerSave", typeof(bool), false, true,
                delegate(Object val)
                {
                    return CmdGetReaderConfiguration(Configuration.TRANSMIT_POWER_SAVE);
                },
                delegate(Object val)
                {
                    CmdSetReaderConfiguration(Configuration.TRANSMIT_POWER_SAVE, val);
                    return val;

                }));
            ParamAdd(new Setting("/reader/antenna/portList", typeof(int[]), null, false,
                delegate(Object val) { return AntennaList; },
                null));
            ParamAdd(new Setting("/reader/antenna/connectedPortList", typeof(int[]), null, false,
                delegate(Object val) { return ConnectedAntennaList; },
                null));
            // Antenna return loss
            ParamAdd(new Setting("/reader/antenna/returnLoss", typeof(int[][]), null, false,
            delegate(Object val) { return CmdGetAntennaReturnLoss(); },
            null));

            //GPIO inputList
            ParamAdd(new Setting("/reader/gpio/inputList", typeof(int[]), null, true,
                delegate(Object val)
                {
                    return getGPIODirection(false);
                },
                delegate(Object val)
                {
                    setGPIODirection(false, (int[])val);
                    return val;
                }
                ));

            //GPIO outputList
            ParamAdd(new Setting("/reader/gpio/outputList", typeof(int[]), null, true,
                delegate(Object val)
                {
                    return getGPIODirection(true);
                },
                delegate(Object val)
                {
                    setGPIODirection(true, (int[])val);
                    return val;
                }
                ));

            ParamAdd(new PortParamSetting(this, "/reader/antenna/settlingTimeList", PortParamSetting.SETTLINGTIME));
            ParamAdd(new Setting("/reader/tagop/protocol", typeof(TagProtocol), TagProtocol.GEN2, true,
                delegate(Object val) { return CurrentProtocol; },
                delegate(Object val)
                {
                    if (!supportedProtocols.Contains((TagProtocol)val))
                    {
                        throw new ReaderException("Unsupported protocol " + val.ToString() + ".");
                    }
                    else
                    {
                        setProtocol((TagProtocol)val);
                        isProtocolDynamicSwitching = false;
                    }
                    return val;
                }));
            ParamAdd(new Setting("/reader/antenna/perAntennaTime", typeof(int[][]), null, true,
            delegate (object val)
            {
                return cmdGetPerAntennaTime();
            },
            delegate (object val)
            {
                cmdSetPerAntennaTime((int[][])val);
                isPerAntTimeSet = true;
                return val;
            }
            ));

            if (!model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/gen2/tagEncoding", typeof(Gen2.TagEncoding), null, true,
                    delegate(Object val)
                    {
                        try { return (Gen2.TagEncoding)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.TAGENCODING); }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { throw new FeatureNotSupportedException("Gen2MillerM not supported"); }
                    },
                    delegate(object val) { CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.TAGENCODING, val); return val; }, false));

                ParamAdd(new Setting("/reader/gen2/q", typeof(Gen2.Q), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.Q); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.Q, val); return val; }, false));

                ParamAdd(new Setting("/reader/gen2/BLF", typeof(Gen2.LinkFrequency), null, true,
                    delegate(Object val)
                    {
                        return Enum.Parse(typeof(Gen2.LinkFrequency), (gen2BLFObjectToInt(CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.LINKFREQUENCY))).ToString(), true);
                    },
                    delegate(Object val)
                    {
                        CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.LINKFREQUENCY, (Object)gen2BLFintToObject((int)val));
                        return val;
                    }
                , false)
                    );

                ParamAdd(new Setting("/reader/gen2/bap", typeof(Gen2.BAPParameters), null, true,
                     delegate(Object val)
                     {
                         try { return (Gen2.BAPParameters)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.BAP); }
                         catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { throw new FeatureNotSupportedException("Gen2 BAP not supported"); }
                     },
                     delegate(object val)
                     {
                         Gen2.BAPParameters bapParams = (Gen2.BAPParameters)val;
                         if (null == bapParams)
                         {
                             // This means user is disabling the BAP support, set the flag to false 
                             // and skip the command sending
                             isBAPEnabled = false;
                             return val;
                         }
                         else if ((bapParams.POWERUPDELAY < -1) || (bapParams.FREQUENCYHOPOFFTIME < -1))
                         {
                             throw new ArgumentException("Invalid values for BAP Paremeters:  " + bapParams.ToString() + ". Accepts only positive values or -1 for NULL.");
                         }
                         else if ((-1 == bapParams.POWERUPDELAY) && (-1 == bapParams.FREQUENCYHOPOFFTIME))
                         {
                             // Here -1 signifies NULL. API should skip the parameter and does not send the matching
                             // command to the reader. Let reader use its own default values
                             try
                             {
                                 isBAPEnabled = true;
                                 throw new Exception("Since the parameters are set to -1 which signifies as NULL and lets module use previous set values.");
                             }
                             catch (Exception ex)
                             {
                                 Console.WriteLine("Parameter [/reader/gen2/bap] error: " + ex.Message);
                             }
                             return val;
                         }
                         else
                         {
                             try
                             {
                                 Gen2.BAPParameters getBapParams = null, setBapParams = null;

                                 // Get BAP params
                                 getBapParams = (Gen2.BAPParameters)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.BAP);
                                 isBAPEnabled = true;

                                 // BAP params to be set
                                 setBapParams = (Gen2.BAPParameters)val;

                                 // Modify the get BAP structure if either of the set BAP params are -1
                                 if (setBapParams.POWERUPDELAY != -1)
                                 {
                                     getBapParams.POWERUPDELAY = setBapParams.POWERUPDELAY;
                                 }
                                 if (setBapParams.FREQUENCYHOPOFFTIME != -1)
                                 {
                                     getBapParams.FREQUENCYHOPOFFTIME = setBapParams.FREQUENCYHOPOFFTIME;
                                 }

                                 // Send the modified BAP structure to the module
                                 CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.BAP, getBapParams); return val;
                             }
                             catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception)
                             {
                                 throw new FeatureNotSupportedException("Gen2 BAP not supported");
                             }
                         }
                     }, false));
            }
            if (M6eFamilyList.Contains(model))
            {
                ParamAdd(new Setting("/reader/gen2/tari", typeof(Gen2.Tari), null, true, delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.TARI); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.TARI, val); return val; }, false)
                    );
                ParamAdd(new Setting("/reader/gen2/protocolExtension", typeof(Gen2.ProtocolExtension), null, false, delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.PROTOCOLEXTENSION); },
                    null, false)
                    );
                ParamAdd(new Setting("/reader/iso180006b/modulationDepth", typeof(Iso180006b.ModulationDepth), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO180006B, Iso180006bConfiguration.MODULATIONDEPTH); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.ISO180006B, Iso180006bConfiguration.MODULATIONDEPTH, val); return val; }, false));
                ParamAdd(new Setting("/reader/iso180006b/delimiter", typeof(Iso180006b.Delimiter), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO180006B, Iso180006bConfiguration.DELIMITER); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.ISO180006B, Iso180006bConfiguration.DELIMITER, val); return val; }, false));
            }
            else if (model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/iso14443a/tagType", typeof(Iso14443a.TagType), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO14443A, Iso14443aConfiguration.TAGTYPE); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.ISO14443A, Iso14443aConfiguration.TAGTYPE, val); return val; }, false));
                ParamAdd(new Setting("/reader/iso14443b/tagType", typeof(Iso14443b.TagType), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO14443B, Iso14443bConfiguration.TAGTYPE); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.ISO14443B, Iso14443bConfiguration.TAGTYPE, val); return val; }, false));
                ParamAdd(new Setting("/reader/iso15693/tagType", typeof(Iso15693.TagType), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO15693, Iso15693Configuration.TAGTYPE); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.ISO15693, Iso15693Configuration.TAGTYPE, val); return val; }, false));
                ParamAdd(new Setting("/reader/iso14443a/supportedTagTypes", typeof(Iso14443a.TagType), null, false,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO14443A, Iso14443aConfiguration.SUPPORTEDTAGTYPES); },
                    null));
                ParamAdd(new Setting("/reader/iso14443b/supportedTagTypes", typeof(Iso14443b.TagType), null, false,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO14443B, Iso14443bConfiguration.SUPPORTEDTAGTYPES); },
                    null));
                ParamAdd(new Setting("/reader/iso15693/supportedTagTypes", typeof(Iso15693.TagType), null, false,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO15693, Iso15693Configuration.SUPPORTEDTAGTYPES); },
                    null));
                ParamAdd(new Setting("/reader/lf125khz/tagType", typeof(Lf125khz.TagType), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.TAGTYPE); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.TAGTYPE, val); return val; }, false));
                ParamAdd(new Setting("/reader/lf125khz/supportedTagTypes", typeof(Lf125khz.TagType), null, false,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.SUPPORTEDTAGTYPES); },
                    null));
                ParamAdd(new Setting("/reader/lf125khz/secureRdFormat", typeof(Lf125khz.NHX_Type), null, true,
                    delegate(Object val)
                    { return CmdGetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.SECUREREADFORMAT); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.SECUREREADFORMAT, val); return val; }, false));
                ParamAdd(new Setting("/reader/lf134khz/tagType", typeof(Lf134khz.TagType), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.LF134KHZ, Lf134khzConfiguration.TAGTYPE); },
                    delegate(Object val) { CmdSetProtocolConfiguration(TagProtocol.LF134KHZ, Lf134khzConfiguration.TAGTYPE, val); return val; }, false));
                ParamAdd(new Setting("/reader/lf134khz/supportedTagTypes", typeof(Lf134khz.TagType), null, false,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.LF134KHZ, Lf134khzConfiguration.SUPPORTEDTAGTYPES); },
                    null));
                ParamAdd(new Setting("/reader/radio/keepRFOn", typeof(bool), false, true,
                    delegate(Object val) { return CmdGetReaderConfiguration(Configuration.CONFIGURATION_KEEP_RF_ON); },
                    delegate(Object val) { CmdSetReaderConfiguration(Configuration.CONFIGURATION_KEEP_RF_ON, val); return val; }));
                ParamAdd(new Setting("/reader/protocolList", typeof(TagProtocol[]), null, true,
                    delegate(Object val) { return CmdGetProtocolList(); },
                    delegate(Object val) { CmdSetProtocolList((TagProtocol[])val); return val; }));
                ParamAdd(new Setting("/reader/iso14443a/supportedTagFeatures", typeof(SupportedTagFeatures), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO14443A, Iso14443aConfiguration.SUPPORTEDTAGFEATURES); },
                    null, false));
                ParamAdd(new Setting("/reader/iso15693/supportedTagFeatures", typeof(SupportedTagFeatures), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.ISO15693, Iso15693Configuration.SUPPORTEDTAGFEATURES); },
                    null, false));
                ParamAdd(new Setting("/reader/lf125khz/supportedTagFeatures", typeof(SupportedTagFeatures), null, true,
                    delegate(Object val) { return CmdGetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.SUPPORTEDTAGFEATURES); },
                    null, false));
            }
            if (!model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/gen2/session", typeof(Gen2.Session), null, true,
                    //delegate(Object val) { return (Gen2.Session)getGen2Session(); },
                    delegate(object val) { return (Gen2.Session)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.SESSION); },
                    //delegate(Object val) { setGen2Session(IntToByte((int)val)); return val; }));
                    delegate(object val) { CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.SESSION, (Gen2.Session)val); return val; }));
                ParamAdd(new Setting("/reader/gen2/target", typeof(Gen2.Target), null, true,
                    delegate(Object val)
                    {
                        try { return GetTarget(); }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { throw new FeatureNotSupportedException("/reader/gen2/target not supported"); }
                    },
                    delegate(Object val) { SetTarget((Gen2.Target)val); return val; }, false));
                ParamAdd(new Setting("/reader/gen2/t4", typeof(UInt32), null, true,
                    delegate(object val) { return (UInt32)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.T4); },
                    delegate(object val)
                    {
                        CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.T4, (UInt32)val);
                        return val;
                    }));
                ParamAdd(new Setting("/reader/gen2/initQ", typeof(Gen2.InitQ), null, true,
                    delegate(object val)
                    {
                        try { return (Gen2.InitQ)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.INITQ); }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { throw new FeatureNotSupportedException("InitQ not supported"); }
                    },
                    delegate(object val)
                    {
                        try
                        {
                            CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.INITQ, (Gen2.InitQ)val);
                        }
                        catch (ReaderCodeException ex) 
                        {
                            throw ex;
                        }
                        return val;
                    }));
                ParamAdd(new Setting("/reader/gen2/sendSelect", typeof(bool), null, true,
                    delegate(object val) { return (Boolean)CmdGetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.SENDSELECT); },
                    delegate(object val)
                    {
                        CmdSetProtocolConfiguration(TagProtocol.GEN2, Gen2Configuration.SENDSELECT, (bool)val);
                        return val;
                    }));
            }
            if (M6eFamilyList.Contains(model))
            {
                ParamAdd(new Setting("/reader/iso180006b/BLF", typeof(Iso180006b.LinkFrequency), Iso180006b.LinkFrequency.LINK160KHZ, true,
                 delegate(Object val)
                 {
                     return Enum.Parse(typeof(Iso180006b.LinkFrequency), (iso18000BBLFObjectToInt(CmdGetProtocolConfiguration(TagProtocol.ISO180006B, Iso180006bConfiguration.LINKFREQUENCY))).ToString(), true);
                 },
                 delegate(Object val)
                 {
                     CmdSetProtocolConfiguration(TagProtocol.ISO180006B, Iso180006bConfiguration.LINKFREQUENCY, iso18000BBLFintToObject((int)val));
                     return val;
                 }, false));
            }

            ParamAdd(new Setting("/reader/version/hardware", typeof(string), null, false,
                delegate(Object val)
                {
                    string hwinfostr = null;
                    try
                    {
                        byte[] hwinfobytes = CmdGetHardwareVersion(0x00, 0x00);
                        hwinfostr = ByteFormat.ToHex(hwinfobytes);
                    }
                    catch (ReaderCodeException) { }
                    return GetHardware(_version);
                    //Remove -0x... from /reader/version/hardware.  Leave only the 4-byte version
                    //number (e.g., 00.00.00.03) 
                    // + ((null != hwinfostr) ? "-" + hwinfostr : ""); // To fix bug 2117 commented the code
                },
                null));
            ParamAdd(new Setting("/reader/version/serial", typeof(string), null, false,
                delegate(Object val)
                {
                    string value = null;
                    try
                    {
                        value = CmdGetSerialNumber();
                    }
                    catch (ReaderCodeException)
                    {
                        // Command failure:
                        // Serial number not implemented on this reader
                        // Leave value at default "no value"
                    }
                    return value;
                }
            , null));
            ParamAdd(new Setting("/reader/version/model", typeof(string), null, false,
                delegate(Object val) { return model; },
                null));
            if (!model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/userMode", typeof(UserMode), null, true,
                    delegate(Object val) { return CmdGetUserMode(); },
                    delegate(Object val) { CmdSetUserMode((UserMode)val); return val; }, false));
            }
            OnLog("Getting current protocol...");
            CurrentProtocol = CmdGetProtocol();

            ParamAdd(new Setting("/reader/read/plan", typeof(ReadPlan), new SimpleReadPlan(), true,
                null,
                delegate(Object val)
                {
                    if (val is SimpleReadPlan)
                    {
                        SimpleReadPlan plan = (SimpleReadPlan)val;
                        if (!validProtocols.Contains(plan.Protocol))
                        {
                            throw new ReaderException("Unsupported protocol " + plan.Protocol + ".");
                        }
                    }
                    else if (val is MultiReadPlan)
                    {
                        MultiReadPlan plan = (MultiReadPlan)val;
                        foreach (SimpleReadPlan var in plan.Plans)
                        {
                            if (!validProtocols.Contains(var.Protocol))
                            {
                                throw new ReaderException("Unsupported protocol " + var.Protocol + ".");
                            }
                        }
                    }
                    else { throw new ArgumentException("Unsupported /reader/read/plan type: " + val.GetType().ToString() + "."); }
                    return val;
                }));
            if (!model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/region/hopTable", typeof(int[]), null, true,
                    delegate(Object val) { return CmdGetFrequencyHopTable(); },
                    delegate(Object val) { CmdSetFrequencyHopTable((uint[])val); return null; }));
                ParamAdd(new Setting("/reader/region/hopTime", typeof(int), null, true,
                    delegate(Object val) { return (int)(UInt32)CmdGetFrequencyHopTime(); },
                    delegate(Object val) { CmdSetFrequencyHopTime((UInt32)(int)val); return null; }));
                ParamAdd(new Setting("/reader/region/quantizationStep", typeof(int), null, true,
                    delegate(Object val) { return (int)(UInt32)CmdGetQuantizationStep(); },
                    delegate(Object val) { CmdSetQuantizationStep((UInt32)(int)val); return null; }));
                ParamAdd(new Setting("/reader/region/minimumFrequency", typeof(int), null, true,
                    delegate(Object val) { return (int)(UInt32)CmdGetMinimumFrequency(); },
                    delegate(Object val) { CmdSetMinimumFrequency((UInt32)(int)val); return null; }));
                ParamAdd(new Setting("/reader/region/lbt/enable", typeof(bool), null, true,
                    delegate(Object val)
                    {
                        try
                        {
                            return CmdGetRegionConfiguration(RegionConfiguration.LBTENABLED);
                        }
                        catch (ReaderCodeException)
                        {
                            // If parameter not supported, assume that LBT is not supported  
                            // and translate to "LBT not enabled" 
                            return false;
                        }
                    },
                    delegate(Object val)
                    {
                        int[] hopTable = (int[])ParamGet("/reader/region/hopTable");
                        int hopTime = -1;
                        try { hopTime = (int)ParamGet("/reader/region/hopTime"); }
                        catch (ArgumentException)
                        {
                            // hopTime doesn't exist before Sontag release
                        }
                        Region region = (Region)ParamGet("/reader/region/id");
                        try
                        {
                            CmdSetRegionLbt(region, (bool)val);
                        }
                        catch (ReaderCodeException ex)
                        {
                            throw ex;
                        }
                        ParamSet("/reader/region/hopTable", hopTable);
                        try { ParamSet("/reader/region/hopTime", hopTime); }
                        catch (ArgumentException)
                        {
                            // hopTime doesn't exist before Sontag release
                        }
                        return null;
                    }, false));

                ParamAdd(new Setting("/reader/region/lbtThreshold", typeof(Int16), null, true,
                    delegate(Object val)
                    {
                        try { return CmdGetRegionConfiguration(RegionConfiguration.LBTTHRESHOLD); }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { return null; }
                    },
                    delegate(Object val)
                    {
                        try
                        {
                            CmdSetLBTThreshold((Region)ParamGet("/reader/region/id"), (Int16)val); return null;
                        }
                        catch (ReaderCodeException)
                        {
                            //removed the below throw as that is not matching with the M5e document guideline error message
                            //throw new ArgumentException("LBT setting not allowed in region " + region.ToString());
                            return false;
                        }
                    }));

                ParamAdd(new Setting("/reader/region/dwellTime/enable", typeof(bool), null, true,
                    delegate(Object val)
                    {
                        try { return CmdGetRegionConfiguration(RegionConfiguration.DWELLENABLE); }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { return null; }
                    },
                   delegate(Object val)
                   {
                       try
                       {
                           CmdSetDwellEnable((Region)ParamGet("/reader/region/id"), (bool)val);
                       }
                       catch (ReaderCodeException ex)
                       {
                           throw ex;
                       }
                       return null;
                   }));

                ParamAdd(new Setting("/reader/region/dwellTime", typeof(UInt16), null, true,
                    delegate(Object val)
                    {
                        try { return CmdGetRegionConfiguration(RegionConfiguration.DWELLTIME); }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { return null; }
                    },
                   delegate(Object val)
                   {
                       try { CmdSetDwellTime((Region)ParamGet("/reader/region/id"), (UInt16)val); }
                       catch (ReaderCodeException) { } return null;
                   }));

                ParamAdd(new PortParamSetting(this, "/reader/radio/portReadPowerList", PortParamSetting.READPOWER));
                ParamAdd(new PortParamSetting(this, "/reader/radio/portWritePowerList", PortParamSetting.WRITEPOWER));
            }
            ParamAdd(new Setting("/reader/radio/readPower", typeof(int), null, true,
                delegate(Object val) { return Convert.ToInt32(CmdGetReadTxPower()); },
                delegate(Object val) { CmdSetReadTxPower((short)(int)val); return val; }));
            ParamAdd(new Setting("/reader/radio/powerMax", typeof(int), null, false,
                delegate(Object val) { return (int)CmdGetReadTxPowerWithLimits()[1]; },
                null));
            ParamAdd(new Setting("/reader/radio/powerMin", typeof(int), null, false,
                delegate(Object val) { return (int)CmdGetReadTxPowerWithLimits()[2]; },
                null));
            ParamAdd(new Setting("/reader/radio/temperature", typeof(int), null, false,
                delegate(Object val) { return (int)CmdGetTemperature(); }, null, false));
            ParamAdd(new Setting("/reader/version/software", typeof(string), null, false,
                delegate(Object val) { return GetSoftware(_version); },
                null));
            ParamAdd(new Setting("/reader/version/supportedProtocols", typeof(TagProtocol[]), null, false,
                delegate(Object val) { return CmdGetAvailableProtocols(); },
                null));
            ParamAdd(new Setting("/reader/region/supportedRegions", typeof(Region[]), null, false,
                delegate(Object val) { return CmdGetAvailableRegions(); },
                null));
            ParamAdd(new Setting("/reader/tagop/antenna", typeof(int), GetFirstConnectedAntenna(), true,
                null,
                delegate(Object val) { return ValidateAntenna((int)val); }));

            ParamAdd(new Setting("/reader/radio/writePower", typeof(int), null, true,
                delegate(Object val) { return Convert.ToInt32(CmdGetWriteTxPower()); },
                delegate(Object val) { CmdSetWriteTxPower((short)(int)val); return val; }));
            ParamAdd(new Setting("/reader/statistics", typeof(ReaderStatistics), null, true,
                delegate(Object val)
                {
                    return CmdGetReaderStatistics(ReaderStatisticsFlag.NOISE_FLOOR_TX_ON
                        | ReaderStatisticsFlag.NOISE_FLOOR
                        | ReaderStatisticsFlag.RF_ON_TIME);
                },
                delegate(Object val) { CmdResetReaderStatistics(ReaderStatisticsFlag.RF_ON_TIME); return val; }));
            ParamAdd(new Setting("/reader/stats/enable", typeof(Reader.Stat.StatsFlag), null, true,
                delegate(Object val)
                {
                    if (isSupportsResetStats)
                    {
                        return statFlag;
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("/reader/stats/enable is not supported");
                    }
                },
                delegate(Object val)
                {
                    if (isSupportsResetStats)
                    {
                        if (model.Equals("M3e"))
                        {
                            Reader.Stat.StatsFlag tempStatsVal = (Reader.Stat.StatsFlag)val;
                            String tempStatsString = tempStatsVal.ToString();
                            if (tempStatsString.IndexOf("ALL") != -1)
                            {
                                tempStatsVal = m3eStatsFlags;
                            }
                            if ((tempStatsVal & m3eStatsFlags) == m3eStatsFlags)
                            {
                                tempStatsVal = m3eStatsFlags;
                            }
                            statFlag = tempStatsVal;
                        }
                        else
                        {
                            statFlag = (Reader.Stat.StatsFlag)val;
                        }
                        return statFlag;
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("/reader/stats/enable is not supported");
                    }
                }));
            ParamAdd(new Setting("/reader/stats", typeof(Reader.Stat), null, true,
                delegate(Object val)
                {
                    if (isSupportsResetStats)
                    {
                        // We should ask for the fields which are requested by the user,
                        // if no fields are requested by the user, then fetch all fields.
                        if (statFlag == Stat.StatsFlag.NONE)
                        {
                            if (model.Equals("M3e"))
                            {
                                statFlag = m3eStatsFlags;
                            }
                            else
                            {
                                statFlag = Stat.StatsFlag.ALL;
                            }
                        }
                        return (Reader.Stat)CmdGetReaderStats(statFlag);
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("/reader/stats is not supported");
                    }
                },
                delegate(Object val)
                {
                    if (isSupportsResetStats)
                    {
                        CmdResetReaderStats((Reader.Stat)val);
                        return val;
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("/reader/stats is not supported");
                    }
                }));
            ParamAdd(new Setting("/reader/metadata", typeof(TagMetadataFlag), null, true,
                delegate(Object val)
                {
                    return metadataFlags;
                },
                delegate(Object val)
                {
                    String metadata = ((TagMetadataFlag)val).ToString();
                    if (model.Equals("M3e"))
                    {
                        if ((metadata.IndexOf("ALL") != -1) || (metadata.IndexOf("PROTOCOL") != -1 && metadata.IndexOf("TAGTYPE") != -1))
                        {
                            TagMetadataFlag userSetMeta = (TagMetadataFlag)val;
                            if (userSetMeta == TagMetadataFlag.ALL)
                            {
                                // If user enters ALL, API sets m3eMetadataFlags.
                                metadataFlags = m3eMetadataFlags;
                                return metadataFlags;
                            }
                            else if ((userSetMeta & m3eMetadataFlags) == userSetMeta)
                            {
                                metadataFlags = userSetMeta;
                                return metadataFlags;
                            }
                            else
                            {
                                throw new Exception("Invalid Argument in \"/reader/metadata\". Please select from " + m3eMetadataFlags.ToString() + ".");
                            }
                        }
                        else
                        {
                            throw new Exception("Invalid Argument in \"/reader/metadata\" : SerialReader.TagMetadataFlag.PROTOCOL and SerialReader.TagMetadataFlag.TAGTYPE are mandatory parameters.");
                        }
                    }
                    else
                    {
                        // Restrict user to set only UHF specific metadata to UHF readers. If user enters TAGTYPE, throw error to user.
                        if (metadata.IndexOf("TAGTYPE") != -1)
                        {
                            throw new Exception("Invalid Argument in \"/reader/metadata\" : " + metadata);
                        }
                        else if (metadata.IndexOf("PROTOCOL") != -1 || metadata.IndexOf("ALL") != -1)
                        {
                            metadataFlags = (TagMetadataFlag)val;
                            return metadataFlags;
                        }
                        else
                        {
                            throw new Exception("Invalid Argument in \"/reader/metadata\" : SerialReader.TagMetadataFlag.PROTOCOL is a mandatory parameter.");
                        }
                    }
                }
                ));
            if (!model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/gen2/writeMode", typeof(Gen2.WriteMode), Gen2.WriteMode.WORD_ONLY, true, null, null));

                // Enable/disable gen2 write response wait time control for early exit/ fixed wait time
                ParamAdd(new Setting("/reader/gen2/writeEarlyExit", typeof(bool), null, true,
                           delegate(Object val)
                           {
                               object[] res;
                               res = CmdGetGen2WriteResponseWaitTime();
                               return (bool)res[0];
                           },
                            delegate(Object val)
                            {
                                CmdSetGen2WriteResponseWaitTime(null, val);
                                return val;
                            }
                            ));
                ParamAdd(new Setting("/reader/gen2/writeReplyTimeout", typeof(int), null, true,
                    delegate(Object val)
                    {
                        object[] res;
                        res = CmdGetGen2WriteResponseWaitTime();
                        return (UInt16)res[1];
                    },
                            delegate(Object val)
                            {
                                if ((int)val < 0)
                                {
                                    throw new ArgumentOutOfRangeException("Negative Timeout Not Supported");
                                }
                                if ((int)val < 1000)
                                {
                                    throw new ArgumentOutOfRangeException(String.Format("Requested timeout ({0}) too low (MinTimeOut={1}microsec)", (int)val, 1000));
                                }
                                if ((int)val > 21000)
                                {
                                    throw new ArgumentOutOfRangeException(String.Format("Requested timeout ({0}) too high (MaxTimeOut={1}microsec)", (int)val, 21000));
                                }
                                // Wait period in micro seconds (max : 21000us, min : 1000us)
                                CmdSetGen2WriteResponseWaitTime(val, null);
                                return val;
                            }
                            ));
            }
            if (!((M6eFamilyList.Contains(model)) || (model.Equals("M3e"))))
            {
                ParamAdd(new Setting("/reader/extendedEpc", typeof(bool), null, true,
                        delegate(Object val)
                        {
                            isExtendedEpc = (bool)CmdGetReaderConfiguration(Configuration.EXTENDED_EPC);
                            return isExtendedEpc;
                        },
                        delegate(Object val)
                        {
                            CmdSetReaderConfiguration(Configuration.EXTENDED_EPC, val);
                            isExtendedEpc = (bool)val;
                            return val;
                        }));
            }
            if (M6eFamilyList.Contains(model))
            {
                ParamAdd(new Setting("/reader/radio/enableSJC", typeof(bool), false, true,
                    delegate(Object val)
                    {
                        return CmdGetReaderConfiguration(Configuration.SELF_JAMMER_CANCELLATION);
                    },
                    delegate(Object val)
                    {
                        CmdSetReaderConfiguration(Configuration.SELF_JAMMER_CANCELLATION, val);
                        return val;
                    }));
                ParamAdd(new Setting("/reader/status/antennaEnable", typeof(bool), false, true,
                     delegate(Object val)
                     {
                         return antennaStatusEnable;
                     },
                     delegate(Object val)
                     {
                         antennaStatusEnable = (bool)val;
                         return val;
                     }));
                ParamAdd(new Setting("/reader/status/frequencyEnable", typeof(bool), false, true,
                    delegate(Object val)
                    {
                        return frequencyStatusEnable;
                    },
                    delegate(Object val)
                    {
                        frequencyStatusEnable = (bool)val;
                        return val;
                    }));
                ParamAdd(new Setting("/reader/status/temperatureEnable", typeof(bool), false, true,
                    delegate(Object val)
                    {
                        return temperatureStatusEnable;
                    },
                    delegate(Object val)
                    {
                        temperatureStatusEnable = (bool)val;
                        return val;
                    }));
                ParamAdd(new Setting("/reader/regulatory/mode", typeof(RegulatoryMode), rMode, true,
                    delegate(Object val)
                    {
                        return rMode;
                    },
                    delegate(Object val)
                    {
                        rMode = (RegulatoryMode)val;
                        return val;
                    }));
                ParamAdd(new Setting("/reader/regulatory/modulation", typeof(RegulatoryModulation), rModulation, true,
                   delegate(Object val)
                   {
                       return rModulation;
                   },
                   delegate(Object val)
                   {
                       rModulation = (RegulatoryModulation)val;
                       return val;
                   }));
                ParamAdd(new Setting("/reader/regulatory/onTime", typeof(int), regOnTime, true,
                   delegate(Object val)
                   {
                       return regOnTime;
                   },
                   delegate(Object val)
                   {
                       if ((int)val < 0 || (int)val > 65535)
                       {
                           throw new ArgumentException("Value of " + val + " to the parameter /reader/regulatory/onTime is out of range.");
                       }
                       regOnTime = (int)val;
                       return val;
                   }));
                ParamAdd(new Setting("/reader/regulatory/offTime", typeof(int), regOffTime, true,
                  delegate(Object val)
                  {
                      return regOffTime;
                  },
                  delegate(Object val)
                  {
                      if ((int)val < 0 || (int)val > 65535)
                      {
                          throw new ArgumentException("Value of " + val + " to the parameter /reader/regulatory/onTime is out of range.");
                      }
                      regOffTime = (int)val;
                      return val;
                  }));
                ParamAdd(new Setting("/reader/regulatory/enable", typeof(bool), false, true,
                  delegate(Object val)
                  {
                      throw new ArgumentException("Unsupported Operation");
                  },
                  delegate(Object val)
                  {
                      cmdTestSendRegulatoryTest(rMode, rModulation, regOnTime, regOffTime, (bool)val);
                      return val;
                  }));
                
                // initializing readFilterTimeout
                Int32 timeout = Convert.ToInt32(CmdGetReaderConfiguration(Configuration.TAG_BUFFER_ENTRY_TIMEOUT));
                _readFilterTimeout = (timeout == 0) ? DEFAULT_READ_FILTER_TIMEOUT : (int)timeout;

                ParamAdd(new Setting("/reader/tagReadData/readFilterTimeout", typeof(int), null, true,
                    delegate(Object val)
                    {
                        return _readFilterTimeout;
                    },
                    delegate(Object val)
                    {
                        int moduleValue = (int)((DEFAULT_READ_FILTER_TIMEOUT == (int)val) ? 0 : val);
                        CmdSetReaderConfiguration(Configuration.TAG_BUFFER_ENTRY_TIMEOUT, moduleValue);
                        _readFilterTimeout = (int)val;
                        return val;
                    }));
            }
            // Retrieve the ENABLE_FILTERING value at connect time from module
            _enableFiltering = (bool)CmdGetReaderConfiguration(Configuration.ENABLE_FILTERING);

            if (M6eFamilyList.Contains(model) || model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/read/trigger/gpi", typeof(int[]), null, true,
                   delegate(Object val)
                   {
                       return CmdGetReaderConfiguration(Configuration.CONFIGURATION_TRIGGER_READ_GPIO);
                   },
                   delegate(Object val)
                   {
                       int value;
                       int[] list = (int[])val;
                       if (list.Length > 0)
                       {
                           value = 1 << (list[0] - 1);
                       }
                       else if (list.Length == 0)
                       {
                           value = 0x00;
                       }
                       else
                       {
                           throw new ArgumentException("Illegal set of GPI for trigger read");
                       }
                       CmdSetReaderConfiguration(Configuration.CONFIGURATION_TRIGGER_READ_GPIO, value);
                       return val;
                   }));
                ParamAdd(new Setting("/reader/licenseKey", typeof(ICollection<byte>), null, true,
                    delegate(Object val) { return null; },
                    delegate(Object val) { CmdSetProtocolLicenseKey((ICollection<byte>)val); return val; }));
                ParamAdd(new Setting("/reader/manageLicenseKey", typeof(LicenseOperation), null, true,
                    delegate(Object val) { return null; },
                    delegate(Object val)
                    {
                        LicenseOperation operation = (LicenseOperation)val;
                        CmdSetProtocolLicenseKey(operation.key, operation.option);
                        return val;
                    }));
                ParamAdd(new Setting("/reader/userConfig", typeof(UserConfigOp), null, true,
                    null,
                    delegate(Object val) { CmdSetUserProfile(((SerialReader.UserConfigOp)val).Opcode, UserConfigCategory.ALL, UserConfigType.CUSTOM_CONFIGURATION); return val; }));
            }
            ParamAdd(new Setting("/reader/tagReadData/enableReadFilter", typeof(bool), true, true,
                    delegate(Object val)
                    {
                        if (model.Equals("M3e"))
                        {
                            _enableFiltering = (bool)CmdGetReaderConfiguration(Configuration.ENABLE_FILTERING);
                        }
                        return _enableFiltering;
                    },
                    delegate(Object val)
                    {
                        if (M6eFamilyList.Contains(model) || model.Equals("M3e"))
                        {
                            CmdSetReaderConfiguration(Configuration.ENABLE_FILTERING, val);
                            _enableFiltering = (bool)val;
                            userEnableReadFiltering = true;
                            return val;
                        }
                        else
                        {
                            throw new ArgumentException("Parameter is read only.");
                        }

                    }));
            ParamAdd(new Setting("/reader/tagReadData/tagopSuccesses", typeof(int), null, false,
                delegate(Object val) { return tagOpSuccessCount; },
                null));
            ParamAdd(new Setting("/reader/tagReadData/tagopFailures", typeof(int), null, false,
                delegate(Object val) { return tagOpFailuresCount; },
                null));

            LoadReaderConfigParams();
            updateAntennaDetection = true;
        }

        #endregion

        #region LoadReaderConfigParams

        private void LoadReaderConfigParams()
        {
            if (!model.Equals("M3e"))
            {
                AddReaderConfigSettingBool("/reader/antenna/checkPort", Configuration.SAFETY_ANTENNA_CHECK);
                //This is not a published parameter.
                //AddReaderConfigSettingBool("CheckPower", Configuration.PA_PROTECT_ENABLE);
                AddReaderConfigSettingBool("/reader/tagReadData/recordHighestRssi", Configuration.RECORD_HIGHEST_RSSI);

                AddReaderConfigSettingBool("/reader/tagReadData/uniqueByData", Configuration.UNIQUE_BY_DATA);
                AddReaderConfigSettingBool("/reader/tagReadData/uniqueByAntenna", Configuration.UNIQUE_BY_ANTENNA);
                AddReaderConfigSettingBool("/reader/tagReadData/reportRssiInDbm", Configuration.RSSI_IN_DBM);
            }
            if (M6eFamilyList.Contains(model))
            {
                AddReaderConfigSettingBool("/reader/tagReadData/uniqueByProtocol", Configuration.UNIQUE_BY_PROTOCOL);
            }
            if (!(M6eFamilyList.Contains(model) || model.Equals("M3e")))
            {
                CmdSetReaderConfiguration(Configuration.RSSI_IN_DBM, true);
            }
        }

        #endregion

        #region ConfigureForProductGroup

        void ConfigureForProductGroup()
        {
            productGroupID = (UInt16)ParamGet("/reader/version/productGroupID");
            if (!model.Equals("M3e"))
            {
                List<int> portSwitchGPIO = new List<int>();
                ParamAdd(new Setting("/reader/antenna/portSwitchGpos", typeof(int[]), null, true,
                    delegate(Object val)
                    {
                        try
                        {
                            byte configByte = (byte)CmdGetReaderConfiguration(Configuration.ANTENNA_CONTROL_GPIO);
                            BitArray gpioListArray = new BitArray(new byte[] { configByte });
                            portSwitchGPIO.Clear();
                            for (int i = 0; i < gpioListArray.Count; i++)
                            {
                                if (gpioListArray[i])
                                    portSwitchGPIO.Add(i + 1);
                            }
                            return portSwitchGPIO.ToArray();
                            //if (portSwitchGPIO.Count != 0)
                            //    return portSwitchGPIO.ToArray();
                            //else
                            //    throw new ArgumentException(String.Format("Unknown Antenna Switch Configuration: 0x{0:2X}", configByte));
                        }
                        catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { throw new FeatureNotSupportedException("/reader/antenna/portSwitchGpos not supported"); }
                    },
                    delegate(Object val)
                    {
                        portmask = new byte();
                        portSwitchGPIO.Clear();
                        portSwitchGPIO.AddRange((int[])val);
                        if (portSwitchGPIO.Count > 0)
                        {
                            for (int i = 0; i < portSwitchGPIO.Count; i++)
                            {
                                portmask |= Convert.ToByte(1 << (portSwitchGPIO[i] - 1));
                            }
                            CmdSetReaderConfiguration(Configuration.ANTENNA_CONTROL_GPIO, portmask);
                            _antdetResult = null;  // Invalidate antenna detect results
                            _txRxMap = MakeDefaultTxRxMap(); //update the txRxMap after setting GPIO
                            isTxRxMapSet = false;
                            if (!isTxRxMapSet)
                            {
                                int[][] temp = _txRxMap.Map;
                                foreach (int[] tuple in temp)
                                {
                                    int virt = tuple[0];
                                    byte tx = (byte)tuple[1];
                                    byte rx = (byte)tuple[2];
                                    int[] txrx = new int[] { tx, rx };
                                    if (!defaultForwardMap.ContainsKey(tx))
                                    {
                                        defaultForwardMap.Add(virt, txrx);
                                    }
                                    byte txrxbyte = (byte)(((tx & 0xF) << 4) | (rx & 0xF));
                                    if ((portmask & 0x04) != 0x00)
                                    {
                                        if (!defaultReverseMap.ContainsKey(txrxbyte))
                                            defaultReverseMap.Add(txrxbyte, virt);
                                    }
                                    else
                                    {
                                        if (!defaultReverseMap.ContainsKey(txrxbyte))
                                        {
                                            defaultReverseMap.Add(txrxbyte, virt);
                                        }
                                    }
                                }
                            }
                            isTxRxMapSet = true;
                            return val;
                        }
                        else if (portSwitchGPIO.Count == 0)
                        {
                            portSwitchGPIO.Clear();
                            CmdSetReaderConfiguration(Configuration.ANTENNA_CONTROL_GPIO, (byte)0x00);
                            _antdetResult = null;  // Invalidate antenna detect results
                            _txRxMap = MakeDefaultTxRxMap(); //update the txRxMap after setting GPIO
                            return val;
                        }
                        else
                            throw new ArgumentException("Unsupported Antenna Switch Configuration");
                    }, false
                    ));
                portSwitchGPIO.Clear();
                portSwitchGPIO.AddRange((int[])ParamGet("/reader/antenna/portswitchgpos"));
                if (portSwitchGPIO.Count != 0)
                {
                    for (int i = 0; i < portSwitchGPIO.Count; i++)
                    {
                        portmask |= Convert.ToByte(1 << (portSwitchGPIO[i] - 1));
                    }
                }
            }
            switch (productGroupID)
            {
                case (UInt16)ProductGroupID.RUGGEDIZED_READER:
                    ParamSet("/reader/antenna/portSwitchGpos", new int[] { 1 });
                    break;
            }

            _txRxMap = MakeDefaultTxRxMap();
            if (!model.Equals("M3e"))
            {
                ParamAdd(new Setting("/reader/antenna/txRxMap", typeof(int[][]), null, true,
                    delegate(Object val) { return _txRxMap.Map; },
                    delegate(Object val)
                    {
                        _txRxMap.Map = (int[][])val;
                        // Mapping may have changed, invalidate current-antenna cache
                        _currentAntenna = 0;
                        _searchList = null;
                        return null;
                    }));
            }
        }

        #endregion

        #region AddReaderConfigSettingBool

        private void AddReaderConfigSettingBool(string paramName, Configuration paramId)
        {
            ParamAdd(MakeReaderConfigSettingBool(paramName, paramId));
        }

        #endregion

        #region MakeReaderConfigSettingBool

        private Setting MakeReaderConfigSettingBool(string paramName, Configuration paramId)
        {
            return new Setting(paramName, typeof(bool), null, true,
                            delegate(Object val)
                            {
                                try
                                {
                                    if (val == null)
                                    {
                                        val = (bool)CmdGetReaderConfiguration(paramId);
                                        switch (paramId)
                                        {
                                            case Configuration.UNIQUE_BY_ANTENNA:
                                                uniqueByAntenna = (bool)val;
                                                break;
                                            case Configuration.UNIQUE_BY_DATA:
                                                uniqueByData = (bool)val;
                                                break;
                                            case Configuration.UNIQUE_BY_PROTOCOL:
                                                uniqueByProtocol = (bool)val;
                                                break;
                                            case Configuration.RECORD_HIGHEST_RSSI:
                                                isRecordHighestRssi = (bool)val;
                                                break;
                                        }
                                    }
                                    return val;
                                }
                                catch (FAULT_MSG_INVALID_PARAMETER_VALUE_Exception) { throw new FeatureNotSupportedException(paramName + " not supported"); }
                            },
                            delegate(Object val)
                            {
                                //if (((MODEL_M6E == _version.Hardware.Part1) ||
                                //    (MODEL_M6E_I == _version.Hardware.Part1) ||
                                //    (MODEL_MICRO == _version.Hardware.Part1) ||
                                //    (MODEL_M6E_NANO == _version.Hardware.Part1))
                                //    && (paramName == "/reader/tagReadData/reportRssiInDbm"))
                                //    throw new FeatureNotSupportedException(paramName + " can not be modified in M6E variants.");
                                CmdSetReaderConfiguration(paramId, val);
                                switch (paramName)
                                {
                                    case "/reader/tagReadData/uniqueByAntenna":
                                        uniqueByAntenna = (bool)val;
                                        break;
                                    case "/reader/tagReadData/uniqueByData":
                                        uniqueByData = (bool)val;
                                        break;
                                    case "/reader/tagReadData/uniqueByProtocol":
                                        uniqueByProtocol = (bool)val;
                                        break;
                                }
                                return val;
                            },
                            false, true);
        }

        #endregion

        #region MakeDefaultTxRxMap

        /// <summary>
        /// Create default TxRxMap
        /// </summary>
        /// <returns>Default TxRxMap
        ///  * All connected ports in monostatic mode
        ///  * Logical antenna number equals TX port number
        /// </returns>
        private TxRxMap MakeDefaultTxRxMap()
        {
            // Create standard TxRxMap (for raw module): 1 mono antenna per physical port
            TxRxMap map = new TxRxMap(this.PortList, this);

            // Modify TxRxMap according to reader product
            UInt16 pgid = productGroupID;
            switch (pgid)
            {
                case 0x0001:
                    // Ruggedized Reader (Tool Link, Vega)
                    map.Map = new int[][]{
                        new int[] {1,2,2},
                        new int[] {2,5,5},
                        new int[] {3,1,1},
                    };
                    break;
                case 0x0002:
                    // If it is M6e Micro USB or M6e Micro USBPro- limit the second antenna port.
                    if (model.Equals("M6e Micro USB"))
                    {
                        map.Map = new int[][]{
                        new int[] {1,1,1}};
                    }
                    else
                    {
                        // USB Reader
                        // Default map okay -- M5e-C only has 1 antenna port, anyway
                    }
                    break;
                default:
                    break;
            }

            return map;
        }

        #endregion

        #endregion

        #region Transport Methods

        #region Connect

        /// <summary>
        /// Connect reader object to device.
        /// If object already connected, then do nothing.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void Connect()
        {
            if (_serialPort.IsOpen)
            {
                OnLog("Serial port " + _serialPort.PortName + " already open");
            }
            else
            {
                OnLog("Calling OpenSerialPort...");
                OpenSerialPort(UriPathToCom(_readerUri), ref currentBaudRate);
                OnLog("Resetting reader...");
                try
                {
                    ResetReader();
                }
                catch (ReaderException ex)
                {
                    if (-1 != ex.Message.IndexOf("Autonomous mode is enabled on reader."))
                    {
                        CmdStopReading();
                        receiveBufferedReads();
                        ResetReader();
                    }
                    else
                    {
                        throw ex;
                    }
                }
                if (universalRegion != Region.UNSPEC)
                {
                    OnLog("Setting region...");
                    ParamSet("/reader/region/id", universalRegion);
                }
                validProtocols.AddRange(CmdGetAvailableProtocols());

                if (TagProtocol.NONE == CurrentProtocol)
                {
                    if (model.Equals("M3e"))
                    {
                        if (validProtocols.Contains(TagProtocol.ISO14443A))
                        {
                            CmdSetProtocol(TagProtocol.ISO14443A);
                            CurrentProtocol = TagProtocol.ISO14443A;
                        }
                    }
                    else
                    {
                        if (validProtocols.Contains(TagProtocol.GEN2))
                        {
                            CmdSetProtocol(TagProtocol.GEN2);
                            CurrentProtocol = TagProtocol.GEN2;
                        }
                    }
                }
                if (model.Equals("M3e"))
                {
                    for (int i = 0; i < validProtocols.Count; i++)
                    {
                        switch (validProtocols[i])
                        {
                            case TagProtocol.ISO14443A:
                                Iso14443a.TagType supportedTagType = (Iso14443a.TagType)CmdGetProtocolConfiguration(TagProtocol.ISO14443A, Iso14443aConfiguration.SUPPORTEDTAGTYPES);
                                protoSuppTagTypeList.Add(TagProtocol.ISO14443A, Convert.ToUInt64(supportedTagType));
                                break;
                            case TagProtocol.ISO15693:
                                Iso15693.TagType suppTagType = (Iso15693.TagType)CmdGetProtocolConfiguration(TagProtocol.ISO15693, Iso15693Configuration.SUPPORTEDTAGTYPES);
                                protoSuppTagTypeList.Add(TagProtocol.ISO15693, Convert.ToUInt64(suppTagType));
                                break;
                            case TagProtocol.LF125KHZ:
                                Lf125khz.TagType lf125khzsuppTagType = (Lf125khz.TagType)CmdGetProtocolConfiguration(TagProtocol.LF125KHZ, Lf125khzConfiguration.SUPPORTEDTAGTYPES);
                                protoSuppTagTypeList.Add(TagProtocol.LF125KHZ, Convert.ToUInt64(lf125khzsuppTagType));
                                break;
                            case TagProtocol.LF134KHZ:
                                Lf134khz.TagType lf134khzsuppTagType = (Lf134khz.TagType)CmdGetProtocolConfiguration(TagProtocol.LF134KHZ, Lf134khzConfiguration.SUPPORTEDTAGTYPES);
                                protoSuppTagTypeList.Add(TagProtocol.LF134KHZ, Convert.ToUInt64(lf134khzsuppTagType));
                                break;
                        }
                    }
                }
                //ParamSet("/reader/tagop/protocol", CurrentProtocol);
                OnLog("Enabling extended EPC...");
                //Enable the extended EPC flag in case of M5E and its variants
                if (!(M6eFamilyList.Contains(model) || (model.Equals("M3e"))))
                {
                    CmdSetReaderConfiguration(Configuration.EXTENDED_EPC, true);
                    isExtendedEpc = true;
                }
            }

            if ((_universalBaudRate != currentBaudRate) && isUserBaudRateSet)
            {
                ChangeBaudRate(_universalBaudRate);
                currentBaudRate = _universalBaudRate;
            }
            else
            {
                _universalBaudRate = currentBaudRate;
            }

            OnLog("Reader connected");
            // Set default metadata flags for M3e. If user sets, this gets overidden with usermetadata.
            if (model == "M3e")
            {
                metadataFlags = m3eMetadataFlags;
                // enable the flag, if model is M3e,
                isModelM3e = true;
            }
        }

        #endregion

        #region ReceiveAutonomousReading
        /// <summary>
        /// Receives data from module continuously
        /// </summary>
        public override void ReceiveAutonomousReading()
        {
            Thread autonomousReadThread = null;
            if (null == autonomousReadThread)
            {
                autonomousReadThread = new Thread(doBackgroundReceiveAutonomousReading);
                autonomousReadThread.IsBackground = true;
                autonomousReadThread.Start();
            }
        }

        private void doBackgroundReceiveAutonomousReading()
        {
            try
            {
                byte[] response = default(byte[]);
                while (true)
                {
                    try
                    {
                        if (baseTime == DateTime.MinValue)
                        {
                            //Update the base time with host time at the time start receiving data.
                            baseTime = DateTime.Now;
                        }
                        response = receiveMessage((byte)CmdOpcode.TAG_READ_MULTIPLE, 0);
                        if (response != null)
                        {
                            ProcessStreamingResponse(response);
                        }
                    }
                    catch (Exception ex)
                    {
                        //If exception is FAULT_MSG_INVALID_PARAMETER_VALUE_Exception, just print the message and continue.
                        if (ex is FAULT_MSG_INVALID_PARAMETER_VALUE_Exception)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }
        #endregion

        #region Reboot
        /// <summary>
        /// Reboots the reader device
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void Reboot()
        {
            // XXX drop down to 9600 before going into the bootloader. Some
            // XXX versions of M5e preserve the baud rate, and some go back to 9600;
            // XXX this way we know what the speed is either way.

            if (!(model.Equals("M3e")))
            {
                ChangeBaudRate(9600);
            }
            try
            {
                CmdBootBootloader();
            }
            catch (FAULT_INVALID_OPCODE_Exception)
            {

                //ignore the case when the module is in BL mode
            }

            // Wait a moment for the bootloader to come back up. This seems to
            // take longer on M5e firmware versions that reset themselves
            // more thoroughly.
            Thread.Sleep(200);
        }
        #endregion

        #region Destroy

        /// <summary>
        /// Shuts down the connection with the reader device.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void Destroy()
        {
            try
            {
                DestroyGivenRead();

                if ((null != this._serialPort) && (this._serialPort.IsOpen))
                {
                    //If crc is not enabled, enable the crc
                    if (!isCRCEnabled)
                    {
                        CmdSetReaderConfiguration(Configuration.SEND_CRC, true);
                    }
                    _serialPort.Shutdown();
                    GC.Collect();
                }
                if (shouldDispose)
                {
                    shouldDispose = false;
                    base.Dispose();
                }
            }
            catch (Exception)
            {
                try
                {
                    // Some serial drivers, such as USB ACM, don't return a disconnect exception
                    // on receive and if we try to shutdown the port throws exception and we are 
                    // ignoring the exception.
                    _serialPort.Shutdown();
                }
                catch { }
            }
        }

        #endregion

        #region SimpleTransportListener

        /// <summary>
        /// Simple console-output transport listener
        /// </summary>
        public override void SimpleTransportListener(Object sender, TransportListenerEventArgs e)
        {
            if (hasTagReportCleared)
            {
                if (_backgroundNotifierCallbackCount!=0)
                {
                    Thread.Sleep(10);
                    hasTagReportCleared = false;
                } 
            }
            Console.WriteLine(String.Format(
                "{0}: {1} (timeout={2:D}ms)",
                e.Tx ? "TX" : "RX",
                ByteFormat.ToHex(e.Data, "", " "),
                e.Timeout
                ));
        }

        #endregion

        #region SetComPort

        /// <summary>
        /// Select serial port
        /// </summary>
        /// <param name="comport">COM Port</param>
        public void SetComPort(string comport)
        {
            _universalComPort = comport;
        }

        #endregion

        #region SetSerialBaudRate

        /// <summary>
        /// Set the baud rate of the serial port in use.  
        ///
        /// NOTE: This is a low-level command and should only be used in
        /// conjunction with CmdSetBaudRate() or CmdBootBootloader()
        /// below. For changing the rate used by the API in general, see the
        /// "/reader/baudRate" parameter.
        /// </summary>
        /// <param name="rate">New serial port speed (bits per second)</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetSerialBaudRate(int rate)
        {
            _universalBaudRate = rate;
            _serialPort.BaudRate = rate;
        }

        #endregion

        #region ChangeBaudRate

        /// <summary>
        /// Change serial speed on both module and host sides
        /// </summary>
        /// <param name="rate">New serial port speed (bits per second)</param>
        private void ChangeBaudRate(int rate)
        {
            //Some SerialTransport device does not support baudrate in such cases skipping setting baudrate. 
            if (_serialPort.BaudRate != 0)
                CmdSetBaudRate((uint)rate);
            SetSerialBaudRate(rate);
        }

        #endregion

        #region OpenSerialPort

        /// <summary>
        /// Initializes Reader Serial Port.
        /// </summary>
        /// <param name="comPort">Reader COM Port</param> 
        /// <param name="currentBaudRate">Reader currentBaudRate</param>
        /// This function opens the COM port passed as a parameter by the user.
        /// If the setBaudRate() function is called before Open(), it will use the new baudrate set in the 
        /// setBaudRate() function.
        public void OpenSerialPort(string comPort, ref int currentBaudRate)
        {
            bool isBaudRateOk = false;
            if (!_serialPort.IsOpen)
            {
                _serialPort.PortName = comPort;
                _serialPort.WriteTimeout = 5000;
                OnLog("Opening serial port " + _serialPort.PortName + "...");
                _serialPort.Open();
                OnLog("Opened serial port " + _serialPort.PortName);
            }

            // Try multiple serial speeds, in the order we're most likely to encounter them
            Exception lastException = null;
            int rate = 0;
            _universalBaudRate = (int)ParamGet("/reader/baudRate");
            int[] bps = probeBaudRates;
            // Get the transport timeout value
            transpTimeout = (int)ParamGet("/reader/transportTimeout");
            //Set transportTimeout to 100 ms until API receives version command response
            if (!userTransportTimeoutEnable)
            {
                ParamSet("/reader/transportTimeout", 100);
            }
            for (int count = 0; count < bps.Length + 2; count++)
            {
                if (count < 2)
                {
                    /* Try this first 
                    Module might be in deep sleep mode, if there is no response for the
                    first attempt, Try the same baudrate again. count = 0 and count = 1 */
                    rate = _universalBaudRate;
                }
                else
                {
                    rate = bps[count - 2];
                    if (_universalBaudRate == rate)
                    {
                        continue;//We already tried this one
                    }
                }
                _serialPort.BaudRate = rate;
                OnLog("Set baud rate to " + _serialPort.BaudRate);

                while (true)
                {
                    try
                    {
                        // Reader, are you there?
                        //_serialPort.DiscardInBuffer();
                        _serialPort.Flush();

                        OnLog("Querying reader...");
                        _version = CmdVersion();
                        //Set the transport Timeout to 5000ms(transTimeout)
                        ParamSet("/reader/transportTimeout", transpTimeout);
                        supportedProtocols = new List<TagProtocol>();
                        foreach (TagProtocol protocol in _version.SupportedProtocols)
                        {
                            supportedProtocols.Add(protocol);
                        }
                        isBaudRateOk = false;
                        currentBaudRate = rate;
                        break;
                    }
                    catch (ReaderCommException ex)
                    {
                        if (ex.Message.StartsWith("Device was reset externally") || ex.Message.StartsWith("Invalid M6e response header, SOH not found in response") || ex.Message.StartsWith("CRC"))
                        {
                            // Incase of 0x22 responses, 100ms transport timeout may not be sufficient.Set it back to 5 secs.
                            ParamSet("/reader/transportTimeout", transpTimeout);
                            cmdStopContinousRead(StrFlushReads);
                        }
                        else
                        {
                            // That didn't work.  Try the next speed, but remember
                            // this exception, in case there's nothing left to try.   
                            lastException = ex;
                            isBaudRateOk = true;
                            break;
                        }
                        continue;
                    }
                    catch (ReaderException re)
                    {
                        if (re.Message.StartsWith("Timeout"))
                        {
                            notifyExceptionListeners(new ReaderException("Failed to connect with baudrate " + rate + " and trying with other baudrates..."));
                            isBaudRateOk = true;
                            break;
                        }
                        else
                        {
                            OnLog("Initial reader query failed.  Closing serial port " + _serialPort.PortName + "...");
                            _serialPort.Shutdown();
                            OnLog("Closed serial port " + _serialPort.PortName);
                            throw re; // A error response to a version command is bad news
                        }
                    }
                }
                //Try next baud rate if this doesn't work
                if (isBaudRateOk)
                {
                    continue;
                }
                if (_version.Hardware.Equals(m4eType1) || _version.Hardware.Equals(m4eType2))
                {
                    model = "M4e";
                }
                else
                {
                    GetModel(_version);
                    if (M6eFamilyList.Contains(model) && (!model.Equals("M6e Nano")))
                    {
                        supportsPreamble = true;
                    }
                }
                return;
            }
            // Nothing worked.  Go ahead and throw that exception.
            OnLog("Reader connect failed.  Closing serial port...");
            _serialPort.Shutdown();
            throw lastException;
        }

        #endregion

        #region SendM5eCommand

        private byte[] SendM5eCommand(ICollection<byte> data)
        {
            int timeout = (int)ParamGet("/reader/commandTimeout");
            return SendTimeout(data, timeout);
        }

        #endregion

        #region SendTimeout

        /// <summary>
        /// Send command to M5e and get response
        /// </summary>
        /// <param name="data">Command to send to M5e, without framing (no SOH, no length, no CRC -- just opcode and arguments)</param>
        /// <param name="timeout">Command timeout -- how long we expect the command itself to take (milliseconds)</param>
        /// <returns>M5e response as byte array, including framing (SOH, length, status, CRC)</returns>
        private byte[] SendTimeout(ICollection<byte> data, int timeout)
        {
            byte[] response = SendTimeoutUnchecked(data, timeout);
            UInt16 status = 0;
            status = ByteConv.ToU16(response, 3);

            GetError(status);

            return response;
        }

        #endregion

        #region SendTimeoutUnchecked

        /// <summary>
        /// Send message and receive response.
        /// </summary>
        /// <param name="data">Command to send to M5e, without framing (no SOH, no length, no CRC -- just opcode and arguments)</param>
        /// <param name="timeout">Command timeout -- how long we expect the command itself to take (milliseconds)</param>
        /// <returns>M5e response as byte array, including framing (SOH, length, status, CRC)</returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        private byte[] SendTimeoutUnchecked(ICollection<byte> data, int timeout)
        {
            UInt16 status = 0;
            sendMessage(data, ref opCode, timeout);
            if (isContinuousReadParamSupported())
            {
                paramMessage.Clear();
                ReceiveResponseStream();
                paramWait = false;
                if (paramMessage.Count >= 5)
                {
                    status = ByteConv.ToU16(paramMessage.ToArray(), 3);
                    if (status != 0 && paramMessage[2] == 0x2f)
                        GetError(status);
                    else if (paramMessage[2] != 0x2f)
                        return null;
                    List<byte> returnReponse = new List<byte>();
                    returnReponse.Add(0xFF);
                    for (int i = 6; i < paramMessage.Count; i++)
                    {
                        returnReponse.Add(paramMessage[i]);
                    }
                    return returnReponse.ToArray();
                }
                else
                    return null;
            }
            return receiveMessage(opCode, timeout);
        }

        #endregion

        #region sendMessage

        /// <summary>
        /// Send message: the transmitting part in "SendTimeoutUnchecked".
        /// </summary>
        /// <param name="data">Command to send to M5e, without framing (no SOH, no length, no CRC -- just opcode and arguments)</param>
        /// <param name="opcode">opcode</param>
        /// <param name="timeout">Command timeout -- how long we expect the command itself to take (milliseconds)</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        private void sendMessage(ICollection<byte> data, ref byte opcode, int timeout)
        {
            /*below is the transmitting portion */

            int transportTimeout = (int)ParamGet("/reader/transportTimeout");
            _serialPort.WriteTimeout = transportTimeout + timeout;
            _serialPort.ReadTimeout = transportTimeout + timeout;

            if ((null == data) || (0 == data.Count))
                throw new ArgumentException("SerialReader commands cannot be empty");
            byte[] outputBuffer = null;
            try
            {
                //Skip Reader Serial Setup if the reader is initialized.
                if (!_serialPort.IsOpen)
                {
                    _serialPort.PortName = _universalComPort;
                    _serialPort.BaudRate = (int)_universalBaudRate;
                    OnLog("sendMessage opening serial port...");
                    _serialPort.Open();
                    OnLog("sendMessage opened serial port");
                }

                /* Wake up processor from deep sleep.  Tickle the RS-232 line, then
         * wait a fixed delay while the processor spins up communications again. */

                if (((powerMode == PowerMode.INVALID) || (powerMode == PowerMode.SLEEP)) && (supportsPreamble))
                {
                    byte[] flushBytes = {
                        (byte)0xFF, (byte)0xFF, (byte)0xFF, (byte)0xFF,(byte)0xFF, (byte)0xFF,(byte)0xFF, (byte)0xFF,
                         };

                    /* Calculate fixed delay in terms of byte-lengths at current speed 
                     @todo Optimize delay length.  This value (100 bytes at 9600bps) is taken
                     directly from arbser, which was itself using a hastily-chosen value.*/
                    //Reducing the number of Iterations of Preambles being sent
                    // int bytesper100ms = _serialPort.BaudRate /50 ;
                    // for (int bytesSent = 0; bytesSent < bytesper100ms; bytesSent += flushBytes.Length)
                    for (int bytesSent = 0; bytesSent < 24; bytesSent++)
                    {
                        try
                        {
                            OnTransport(true, flushBytes, _serialPort.WriteTimeout);
                            _serialPort.SendBytes(flushBytes.Length, flushBytes, 0);
                            // Introducing 9 ms delay between each Iteration
                            Thread.Sleep(9);
                        }
                        catch (Exception ex)
                        {
                            throw ex;
                        }
                    }
                }

                if (isContinuousReadParamSupported())
                {
                    paramWait = true;
                    paramMessage.Clear();
                    paramMessage.AddRange(new byte[] { 0x2F, 0x00, 0x00, 0x04 });
                    paramMessage.Add(Convert.ToByte(data.Count - 1));
                    paramMessage.AddRange(data);
                    outputBuffer = BuildM5eMessage(paramMessage.ToArray());  //assembly command
                }
                else
                {
                    outputBuffer = BuildM5eMessage(data);  //assembly command
                    opcode = outputBuffer[2]; //stores command opCode for comparison
                }

                if (outputBuffer.Length>=256)
                {
                    throw new Exception("Command out of index range.");
                }
                OnTransport(true, outputBuffer, _serialPort.WriteTimeout);  //calling sending listener
                _serialPort.SendBytes(outputBuffer.Length, outputBuffer, 0);  //sending cmd

            }
            catch (Exception ex)
            {
                if (ex is TimeoutException)
                {
                    OnTransport(true, outputBuffer, _serialPort.WriteTimeout);
                    throw new ReaderCommException(ex.Message, outputBuffer);
                }
                if (ex is InvalidOperationException)
                {
                    throw new ReaderCommException(ex.Message, outputBuffer);
                }
                if (ex is IOException)
                {
                    uint errCode = (uint)System.Runtime.InteropServices.Marshal.GetHRForException(ex);
                    if (_runNow)
                    {
                        ReadExceptionPublisher rx = null;
                        ReaderException exception = new ReaderException(ex.Message);
                        {
                            if (ErrorCodePort1DoesNotExist == errCode || ErrorCodeDeviceNotRecognized == errCode
                                || ErrorCodeDeviceNotFunctioning == errCode)
                            {
                                exception = new ReaderException("Specified port does not exist.");
                            }
                        }
                        if (_isPseudoAsyncRead)
                        {
                            throw exception;
                        }
                        rx = new ReadExceptionPublisher(this, exception);
                        Thread trd = new Thread(rx.OnReadException);
                        trd.Start();
                    }
                    else if (ErrorCodePort1DoesNotExist == errCode || ErrorCodeDeviceNotRecognized == errCode
                        || ErrorCodeDeviceNotFunctioning == errCode)
                    {
                        throw new ReaderException("Specified port does not exist.");
                    }
                    else
                    {
                        throw ex;
                    }
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
        }

        #endregion

        #region receiveMessage
        /// <summary>
        /// Receive message: the receiving part in "SendTimeoutUnchecked".
        /// </summary>
        /// <param name="opcode">opcode</param>
        /// <param name="timeout">Command timeout -- how long we expect the command itself to take (milliseconds)</param>
        /// <returns>M5e response as byte array, including framing (SOH, length, status, CRC)</returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        private byte[] receiveMessage(byte opcode, int timeout)
        {
            /*below is the receiving portion */


            byte[] inputBuffer = new byte[256];
            int responseLength = 0;
            int sohIndex = 0;
            int transportTimeout = 0;
            int messageLength = 0;
            int inLen = 0;
            int retryCount = 0;
            bool sohFound = false;
            byte argLength = 0x00;
            try
            {
                transportTimeout = (int)ParamGet("/reader/transportTimeout");
                if (opcode == 0x22)
                {
                    timeout += (int)ParamGet("/reader/read/asyncOffTime");
                }
                _serialPort.WriteTimeout = transportTimeout + timeout;
                _serialPort.ReadTimeout = transportTimeout + timeout;

                if (isCRCEnabled)
                {
                    messageLength = 7;
                }
                else
                {
                    messageLength = 5;
                }

                do
                {
                    //pull at least messageLength bytes on first serial receive
                    inLen += ReadAll(_serialPort, inputBuffer, inLen, (messageLength - inLen));

                    if (inLen < messageLength)
                    {
                        // SOH not Found or data not received..continuing Retry!!!
                        continue;
                    }
                    // Search for SOH(0xFF)
                    for (sohIndex = 0; sohIndex < (messageLength - 2); sohIndex++)
                    {
                        /* <Valid SOH(0xFF)> + <Valid length(Less than 0xF8)> + <Valid OPCODE> */
                        if ((0xFF == inputBuffer[sohIndex + 0]) && (inputBuffer[sohIndex + 1] <= 0xF8))
                        {
                            if ((inputBuffer[sohIndex + 2] == opcode)
                               || (inputBuffer[sohIndex + 2] == 0x22)
                               || (inputBuffer[sohIndex + 2] == 0x2F)
                               || (inputBuffer[sohIndex + 2] == 0x9D))
                            {
                                // If SOH is found at 0th index, exit the loop
                                if (sohIndex == 0)
                                {
                                    sohFound = true;
                                }
                                break; /* EXIT FOR */
                            }
                        }
                    }

                    if (sohFound)
                    {
                        break;
                    }
                    else
                    {
                        // Update inLen with correct length after discarding invalid bytes
                        inLen = messageLength - sohIndex;

                        // Now, copy the inlen number of bytes to data buffer at 0th index.
                        Array.Copy(inputBuffer, sohIndex, inputBuffer, 0, inLen);
                    }

                } while (++retryCount < 20);

                if (retryCount >= 20)
                {
                    throw new ReaderException(String.Format("TimeoutException"));
                }
                if (sohFound == false)
                {
                    throw new ReaderCommException(String.Format("Invalid M6e response header, SOH not found in response."), new byte[0]);
                }

                /*if no bytes are read, return null*/
                if (inLen == 0)
                {
                    return null;
                }

                argLength = inputBuffer[1];

                if (isCRCEnabled)
                {
                    // Total response = SOH, length, opcode, status, CRC
                    responseLength = argLength + 1 + 1 + 1 + 2 + 2;
                }
                else
                {
                    // Total response = SOH, length, opcode, status
                    responseLength = argLength + 1 + 1 + 1 + 2;
                }
                if ((inputBuffer.Length - messageLength) < argLength)
                {
                    //packet data size is overflowing the buffer size. This could be a
                    //corrupted packet. Discard it and move on. Return back with Value too big
                    //error.
                    throw new ReaderException("packet size is too big");
                }
                //Now pull in the rest of the data, if exists, + the CRC
                if (argLength != 0)
                {
                    inLen += ReadAll(_serialPort, inputBuffer, inLen, argLength);
                }
            }

            catch (Exception ex)
            {
                if (ex is TimeoutException)
                {
                    OnTransport(false, CropBuf(inputBuffer, inLen), _serialPort.ReadTimeout); //calling receiving listener
                    throw new ReaderCommException(ex.Message, CropBuf(inputBuffer, inLen));
                }
                if (ex is InvalidOperationException)
                {
                    if (ex.Message == "The port is closed.")
                    {
                        return default(byte[]);
                    }
                    throw new ReaderCommException(ex.Message, CropBuf(inputBuffer, inLen));
                }
                if (ex is IOException)
                {
                    if (_runNow)
                    {
                        ReadExceptionPublisher rx = null;
                        ReaderException exception = new ReaderException(ex.Message);
                        {
                            // when exception received is Error: The I/O operation has been 
                            // aborted because of either a thread exit or an application request.
                            uint errCode = (uint)System.Runtime.InteropServices.Marshal.GetHRForException(ex);
                            if (ErrorCodePortDoesNotExist == errCode || ErrorCodeDeviceNotRecognized == errCode
                                || ErrorCodeDeviceNotFunctioning == errCode)
                            {
                                exception = new ReaderException("Specified port does not exist.");
                            }
                        }
                        if (_isPseudoAsyncRead)
                        {
                            throw exception;
                        }
                        rx = new ReadExceptionPublisher(this, exception);
                        Thread trd = new Thread(rx.OnReadException);
                        trd.Start();
                    }
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
            if (model == "M3e" && opCode == 0x06)
            {
                Thread.Sleep(100);
            }
            OnTransport(false, CropBuf(inputBuffer, inLen), _serialPort.ReadTimeout); //calling receiving listener
            byte[] response = null;
            UInt16 status = 0;
            // Check for return message CRC
            if (responseLength > 0)
            {
                byte[] returnCRC = new byte[2];

                if (isCRCEnabled)
                {
                    byte[] inputBufferNoCRC = new byte[(responseLength - 2)];
                    Array.Copy(inputBuffer, 0, inputBufferNoCRC, 0, (responseLength - 2));
                    try
                    {
                        returnCRC = CalcReturnCRC(inputBufferNoCRC);
                    }
                    catch { };
                    if (ByteConv.ToU16(inputBuffer, (responseLength - 2)) != ByteConv.ToU16(returnCRC, 0))
                    {
                        throw new ReaderCommException("CRC Error", CropBuf(inputBuffer, inLen));
                    }
                }

                if (_debug == 1)
                {
                    for (int i = 0; i < inputBuffer[1] + messageLength; i++)
                        Console.Write("{0:X2}", inputBuffer[i]);
                    Console.WriteLine("");
                }

                response = new byte[responseLength];
                Array.Copy(inputBuffer, response, responseLength);

                /* We got a response for a different command than the one we
                 * sent. This usually means we received the boot-time message from
                 * a M6e, and thus that the device was rebooted somewhere between
                 * the previous command and this one. Report this as a problem.
                 */
                if ((response[2] != opcode) && (response[2] != 0x2F || !enableStreaming))
                {
                    if (response[2] == 0x9D)
                    {
                        throw new ReaderException("Autonomous mode is enabled on reader. Please disable it.");
                    }
                    else if (response[2] == 0x04)
                    {
                        throw new ReaderCommException(String.Format("Boot response received."));
                    }
                    else
                    {
                        throw new ReaderCommException(String.Format("Device was reset externally. " +
                            "Response opcode {0:X2} did not match command opcode {1:X2}. ", response[2], opCode));
                    }
                }

                status = ByteConv.ToU16(response, 3);
            }

            try
            {
                //Don't catch TMR_ERROR_NO_TAGS_FOUND for ReadTagMultiple.
                if (status != FAULT_NO_TAGS_FOUND_Exception.StatusCode)
                {
                    if (response[2] == 0x2f)
                    {
                        if (response.Length > 5)
                        {
                            if (response[5] == 0x04)
                            {
                                return response;
                            }
                        }
                    }
                    GetError(status);
                }
            }
            catch (FAULT_TM_ASSERT_FAILED_Exception)
            {
                throw new FAULT_TM_ASSERT_FAILED_Exception(String.Format("Reader assert 0x{0:X} at {1}:{2}", status,
                    ByteConv.ToAscii(response, 9, response.Length - 9), ByteConv.ToU32(response, 5)));
            }
            catch (FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception)
            {
                throw new FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception(response);
            }

            return response;
        }

        /// <summary>
        /// Function to receive the first streaming response after serial port opened and initializes certain variables
        /// </summary>
        /// <param name="serial"></param>
        /// <param name="mdl"></param>
        public void receiveResponse(SerialTransport serial, string mdl)
        {
            AssignStatusFlags();
            _serialPort = serial;
            byte[] response = new byte[256];
            response = receiveMessage(0x22, 1000);

            if (response[5]==(byte)(0x88))
            {
                enableMultipleSelect = true;
            }

            autonomousStreaming = true;
 
        }

        #endregion

        #region isContinuousReadParamSupported

        bool isContinuousReadParamSupported()
        {
            try
            {
                if (hasContinuousReadStarted == false || isTrueContinuousRead == false)
                    return false;

                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M6E:
                    case MODEL_M6E_I:
                        checkVersion = new Version("1.33.1.25");
                        break;
                    case MODEL_MICRO:
                        checkVersion = new Version("1.9.0.20");
                        break;
                    case MODEL_M6E_NANO:
                        checkVersion = new Version("1.7.0.14");
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }


        #endregion

        #region ReadAll

        /// <summary>
        /// Read from serial port until all requested bytes have arrived.
        /// </summary>
        /// <param name="ser"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private static int ReadAll(SerialTransport ser, byte[] buffer, int offset, int count)
        {
            int bytesReceived = 0;
            int exitLoop = 0;
            int iterations = 0;

            try
            {
                while (bytesReceived < count && exitLoop == 0)
                {
                    iterations += 1;
                    debug.Log("SerialReader.ReadAll iter#" + iterations);
                    bytesReceived += ser.ReceiveBytes(count - bytesReceived, buffer, offset + bytesReceived);
                }
            }
            catch (Exception ex)
            {
                uint errCode = (uint)System.Runtime.InteropServices.Marshal.GetHRForException(ex);
                if (ErrorCodeSocketIOException == errCode)
                {
                    throw new TimeoutException("Timeout");
                }
                else
                {
                    throw ex;
                }
            }
            return bytesReceived;
        }

        #endregion

        #region DebugMode

        /// <summary>
        /// Select debugging level
        /// </summary>
        /// <param name="onOff">1 for on, 0 for off</param>
        public void DebugMode(int onOff)
        {
            _debug = onOff;
        }

        #endregion

        #endregion

        #region M5e Command Set

        #region BuildM5eMessage

        //*******************************************************
        //*              Build M5e Message                      *
        //*******************************************************

        /// <summary>
        /// Builds M5e Message by adding SOF, Length and CRC.
        /// </summary>
        /// <param name="data">Byte array of M5e Opcode and Data</param>
        /// <returns>Byte Array of the full message with SOF, Length, OpCode, Data and CRC</returns>
        private static byte[] BuildM5eMessage(ICollection<byte> data)
        {
            int nBytes = data.Count;
            byte[] m5eMessage = new byte[2 + nBytes + 2];
            // byte[] m5eMessage = new byte[2 + nBytes + 2 + 1/*DUMMY 0*/];

            m5eMessage[0] = 0xFF;
            m5eMessage[1] = (byte)(nBytes - 1);

            // 8/4/2009 - JSnyder
            //          - if Framework 3.5 or greater, use Collection.ToArray<>, else use our static generic ToArray<>() method;
            Array.Copy(CollUtil.ToArray(data), 0, m5eMessage, 2, nBytes);

            Array.Copy(CalcCRC(m5eMessage), 0, m5eMessage, nBytes + 2, 2);


            return m5eMessage;
        }

        #endregion

        #region GetDataFromM5eResponse

        //*******************************************************
        //*         Strip Return Data from M5e Response         *
        //*******************************************************

        /// <summary>
        /// Strips the Data from the M5e response byte array.
        /// </summary>
        /// <param name="response">Complete Response array returned by the M5e including header, Length, OpCode,
        /// status, CRC</param>
        /// <returns>Data without header, Length, OpCode, status, CRC</returns>

        private static byte[] GetDataFromM5eResponse(byte[] response)
        {
            if (null == response) return default(byte[]);
            byte[] returnData = new byte[(response[1])];
            Array.Copy(response, 5, returnData, 0, response[1]);
            return returnData;
        }

        #endregion

        //****************************************************************
        //* All the Serial commands to the M5e are defined below.
        //****************************************************************

        #region Bootloader Commands

        #region FirmwareLoad

        /// <summary>
        /// Load a new firmware image into the device's nonvolatile memory.
        /// This installs the given image data onto the device and restarts
        /// it with that image. The firmware must be of an appropriate type
        /// for the device. Interrupting this operation may damage the
        /// reader.
        /// </summary>
        /// <param name="firmware">a data _stream of the firmware contents</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void FirmwareLoad(System.IO.Stream firmware)
        {
            //if firmware is corrupted, then open the serial port and load the firmware
            if (!_serialPort.IsOpen)
            {
                OpenSerialPort(UriPathToCom(_readerUri), ref currentBaudRate);
            }

            BinaryReader fwr = new BinaryReader(firmware);
            UInt32 header1 = ByteIO.ReadU32(fwr);
            UInt32 header2 = ByteIO.ReadU32(fwr);

            // Check the magic numbers for correct magic number
            if ((header1 != 0x544D2D53) || (header2 != 0x5061696B))
                throw new ArgumentException("Stream does not contain reader firmware");

            UInt32 sector = ByteIO.ReadU32(fwr);
            UInt32 len = ByteIO.ReadU32(fwr);

            if (sector != 2)
                throw new ArgumentException("Only application firmware can be loaded");

            // Move the reader into the bootloader so flash operations work
            // XXX drop down to 9600 before going into the bootloader. Some
            // XXX versions of M5e preserve the baud rate, and some go back to 9600;
            // XXX this way we know what the speed is either way.
            ChangeBaudRate(9600);
            
            try
            {
                CmdBootBootloader();  //must be in BL to update firmware
            }
            catch (FAULT_INVALID_OPCODE_Exception)
            {
                //ignore the case when the module is in BL mode
            }

            // Wait a moment for the bootloader to come back up. This seems to
            // take longer on M5e firmware versions that reset themselves
            // more thoroughly.
            Thread.Sleep(200);

            bool currentPreambleValue = supportsPreamble;
            // Bootloader doesn't support wakeup preambles 
            supportsPreamble = false;

            ChangeBaudRate(115200);
            CmdEraseFlash(2, 0x08959121);

            UInt32 address = 0;
            //System.IO.StreamWriter file = new System.IO.StreamWriter("c:\\test.txt");

            while (len > 0)
            {
                byte[] chunk = fwr.ReadBytes(240);
                if (0 == chunk.Length)
                    throw new ArgumentException("Firmware _stream too short.  Ran out of data before reaching declared length.");

                CmdWriteFlash(2, address, 0x02254410, chunk.Length, chunk, 0);
                address += (uint)chunk.Length;
                len -= (uint)chunk.Length;
                //Thread.Sleep(100);
                //file.WriteLine("{0}", address);
                //file.Flush();
            }

            supportsPreamble = currentPreambleValue;

            //// Boot the application and re-initialize parameters that may be changed by the new firmware
            try
            {
                ResetReader();
            }
            catch (ReaderException ex)
            {
                if (-1 != ex.Message.IndexOf("Autonomous mode is enabled on reader."))
                {
                    Console.WriteLine("Firmware update is successful. Autonomous mode is already enabled on reader");
                    cmdStopContinousRead(StrFlushReads);
                    ResetReader();
                }
                else
                {
                    throw ex;
                }
            }
        }

        /// <summary>
        /// Loads firmware on the Reader.
        /// </summary>
        /// <param name="firmware">Firmware IO stream</param>
        /// <param name="flOptions">firmware load options</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void FirmwareLoad(Stream firmware, FirmwareLoadOptions flOptions)
        {
            throw new ReaderException("Not supported");
        }
        #endregion

        #region CmdBootFirmware

        /// <summary>
        /// Tell the boot loader to verify the application firmware and execute it.
        /// </summary>
        public void CmdBootFirmware()
        {
            byte[] data = new byte[1];
            data[0] = 0x04;
            _version = ParseVersionResponse(GetDataFromM5eResponse(SendM5eCommand(data)));
            supportedProtocols.Clear();
            foreach (TagProtocol protocol in _version.SupportedProtocols)
            {
                supportedProtocols.Add(protocol);
            }
        }

        #endregion

        #region CmdGetGen2WriteResponseWaitTime

        /// <summary>
        /// Parse get gen2 writeresponse wait time response
        /// </summary>
        public object[] CmdGetGen2WriteResponseWaitTime()
        {
            List<byte> data = new List<byte>();
            List<object> parseResponseList = new List<object>();
            byte[] response = new byte[8];
            // Form a message to get gen2 writeresponse waittime
            data.Add(0x6b);
            data.Add(0x05);
            data.Add(0x3f);
            // Send the message to the reader
            response = GetDataFromM5eResponse(SendM5eCommand(data));

            // Parse gen2 writeresponse wait time response
            // Writeearlyexit
            parseResponseList.Add(!Convert.ToBoolean(response[2]));

            // Waitresponse timeout
            List<byte> msgRes = new List<byte>();
            msgRes.Add(response[3]);
            msgRes.Add(response[4]);
            byte[] resWaitTime = msgRes.ToArray();
            Array.Reverse(resWaitTime);
            parseResponseList.Add(BitConverter.ToUInt16(resWaitTime, 0));
            return parseResponseList.ToArray();
        }
        #endregion CmdGetGen2WriteResponseWaitTime

        #region CmdSetGen2WriteResponseWaitTime

        /// <summary>
        /// Set gen2 writeresponse wait time
        /// </summary>
        public void CmdSetGen2WriteResponseWaitTime(object waitTime, object writeEarlyExit)
        {
            object[] response = null;
            List<byte> msg = new List<byte>();
            response = CmdGetGen2WriteResponseWaitTime();
            msg.Add(0x9b);
            msg.Add(0x05);
            msg.Add(0x3f);
            //Add writeEarlyExit to message
            if (null != writeEarlyExit)
            {
                msg.Add(Convert.ToByte(!(bool)writeEarlyExit));
            }
            else
            {
                msg.Add(Convert.ToByte(!(bool)response[0]));
            }
            //Add waitTime out to message
            if (null != waitTime)
            {
                msg.AddRange(ByteConv.EncodeU16(Convert.ToUInt16(waitTime)));
            }
            else
            {
                msg.AddRange(ByteConv.EncodeU16((UInt16)response[1]));
            }
            // Send the framed message to the reader
            SendM5eCommand(msg);
        }
        #endregion CmdSetGen2WriteResponseWaitTime

        #region CmdBootBootloader

        /// <summary>
        /// Quit running the application and execute the bootloader. Note
        /// that this changes the reader's baud rate to 9600; the user is
        /// responsible for changing the local serial sort speed.
        /// </summary>
        public void CmdBootBootloader()
        {
            byte[] data = new byte[1];
            data[0] = 0x9;
            isCRCEnabled = true;
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #region CmdSetBaudRate

        /// <summary>
        /// Tell the device to change the baud rate it uses for
        /// communication. Note that this does not affect the host side of
        /// the serial interface; it will need to be changed separately.
        /// </summary>
        /// <param name="bps">the new baud rate to use.</param>
        /// Initially the UniversalBaudRate is 9600 which is the default on the reader when the application is loaded.
        /// After setBaudRate() function is called, the embedded reader is set to the new baudrate and the 
        /// UniversalBaudRate is set to the new baudrate. Subsequent commands to the embedded reader will now use
        /// the new baudrate. m5e.Baudrate is set to the new baudrate.
        public void CmdSetBaudRate(UInt32 bps)
        {
            byte[] data = new byte[5];
            data[0] = 0x6;
            ByteConv.FromU32(data, 1, bps);
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #region CmdEraseFlash
        /// <summary>
        /// Erase a sector of the device's flash.
        /// </summary>
        /// <param name="sector">the flash sector, as described in the embedded module user manual</param>
        /// <param name="password">the erase password for the sector</param>
        public void CmdEraseFlash(byte sector, UInt32 password)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x07);
            cmd.AddRange(ByteConv.EncodeU32(password));
            cmd.Add(sector);
            SendTimeout(cmd, 30000);
        }
        #endregion

        #region CmdWriteFlash

        /// <summary>
        /// Write data to a previously erased region of the device's flash.
        /// </summary>
        /// <param name="sector">the flash sector, as described in the embedded module user manual</param>
        /// <param name="address">the byte address to start writing from</param>
        /// <param name="password">the write password for the sector</param>
        /// <param name="length">the amount of data to write. Limited to 240 bytes.</param>
        /// <param name="data">the data to write (from offset to offset + length - 1)</param>
        /// <param name="offset">the index of the data to be writtin in the data array</param>
        public void CmdWriteFlash(byte sector, UInt32 address, UInt32 password, int length, byte[] data, int offset)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x0D);
            cmd.AddRange(ByteConv.EncodeU32(password));
            cmd.AddRange(ByteConv.EncodeU32(address));
            cmd.Add(sector);
            byte[] finaldata = new byte[length];
            Array.Copy(data, offset, finaldata, 0, length);
            cmd.AddRange(finaldata);
            SendTimeout(cmd, 3000);
        }
        #endregion

        #region CmdModifyFlash

        /// <summary>
        /// Write data to the device's flash, erasing if necessary.
        /// </summary>
        /// <param name="sector">the flash sector, as described in the embedded module user manual</param>
        /// <param name="address">the byte address to start writing from</param>
        /// <param name="password">the write password for the sector</param>
        /// <param name="length">the amount of data to write. Limited to 240 bytes.</param>
        /// <param name="data">the data to write (from offset to offset + length - 1)</param>
        /// <param name="offset">the index of the data to be writtin in the data array</param>
        public void CmdModifyFlash(byte sector, UInt32 address, UInt32 password, int length, byte[] data, int offset)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x0F);
            cmd.AddRange(ByteConv.EncodeU32(password));
            cmd.AddRange(ByteConv.EncodeU32(address));
            cmd.Add(sector);
            byte[] finaldata = new byte[length];
            Array.Copy(data, offset, finaldata, 0, length);
            cmd.AddRange(finaldata);
            SendTimeout(cmd, 3000);
        }

        #endregion

        #region CmdReadFlash
        /// <summary>
        /// Read the contents of flash from the specified address in the specified flash sector.
        /// </summary>
        /// <param name="sector">the flash sector, as described in the embedded module user manual</param>
        /// <param name="address">the byte address to start reading from</param>
        /// <param name="length">the number of bytes to read. Limited to 248 bytes.</param>
        /// <returns>the read data</returns>
        public byte[] CmdReadFlash(byte sector, UInt32 address, byte length)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x02);
            cmd.AddRange(ByteConv.EncodeU32(address));
            cmd.Add(sector);
            cmd.Add(length);
            return GetDataFromM5eResponse(SendTimeout(cmd, 3000));
        }
        #endregion

        #region CmdGetSectorSize

        /// <summary>
        /// Return the size of a flash sector of the device.
        /// </summary>
        /// <param name="sector">the flash sector, as described in the embedded module user manual</param>
        /// <returns>Size of the Sector</returns>
        public UInt32 CmdGetSectorSize(byte sector)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x0E);
            cmd.Add(sector);
            return ByteConv.ToU32(GetDataFromM5eResponse(SendM5eCommand(cmd)), 0);
        }
        #endregion

        #region CmdVerifyImage

        /// <summary>
        /// Verify that the application image in flash has a valid checksum.
        /// The device will report an invalid checksum with a error code
        /// response, which would normally generate a ReaderCodeException;
        /// this routine traps that particular exception and simply returns
        /// "false".
        /// </summary>
        /// <returns>whether the image is valid</returns>
        public bool CmdVerifyImage()
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x08);
            try
            {
                SendM5eCommand(cmd);
                return true;
            }
            catch (FAULT_MSG_WRONG_NUMBER_OF_DATA_Exception)
            {
                return false;
            }
            catch (FAULT_BL_INVALID_IMAGE_CRC_Exception)
            {
                return false;
            }
        }

        #endregion

        #endregion

        #region General Commands

        #region ResetReader

        private void ResetReader()
        {
            // TODO: ParamClear breaks pre-connect params.
            // ParamClear was orginally intended to reset the parameter table
            // after a firmware update, removing parameters that no longer exist
            // in the new firmware.  Deprioritize post-upgrade behavior for now.
            // --Harry Tsai 2009 Jun 26
            //ParamClear();
            SendInitCommands();

            // This version check is required for the new reader stats.
            // Older firmwares does not support this. Currently, firmware version
            // 1.21.1.2 has support for new reader stats.
            if (((model.Equals("M6e") || model.Equals("M6e PRC") || model.Equals("M6e JIC")) && _version.Firmware.CompareTo(new VersionNumber((byte)1, (byte)21, (byte)1, (byte)2)) > 0) || ((model.Equals("M6e Micro") || model.Equals("M6e Micro USB") || model.Equals("M6e Micro USBPro")) && _version.Firmware.CompareTo(new VersionNumber((byte)1, (byte)3, (byte)0, (byte)20)) >= 0) || ((model.Equals("M6e Nano")) && _version.Firmware.CompareTo(new VersionNumber((byte)1, (byte)3, (byte)2, (byte)74)) >= 0) || (model.Equals("M3e")))
            {
                isSupportsResetStats = true;
            }
            else
            {
                isSupportsResetStats = false;
            }
            isMultipleSelectSupported = IsMultipleSupported();
            {
                checkForSupportedFeatures();
            }
            LoadParams();
        }

        #endregion

        #region SendInitCommands

        private void SendInitCommands()
        {
            byte program_status = 0;

            try
            {
                program_status = CmdGetCurrentProgram();
                if ((program_status & 0x3) == 1)
                {
                    CmdBootFirmware();
                }
                else if ((program_status & 0x3) != 2)
                {
                    throw new ReaderException(String.Format("Unknown current program 0x{0:X}", program_status));
                }
            }
            catch (ReaderCodeException ex)
            {
                if (ex is FAULT_TM_ASSERT_FAILED_Exception)
                {
                    throw ex;
                }
                try
                {
                    CmdBootFirmware();
                }
                catch (FAULT_INVALID_OPCODE_Exception)
                {
                    // Ignore "invalid opcode" -- that just means app is already booted
                }
                catch (FAULT_BL_INVALID_IMAGE_CRC_Exception)
                {
                    throw;
                }
            }
            /* Initialize cached power mode value */
            /* Should read power mode as soon as possible.
             * Default mode assumes module is in deep sleep and
             * adds a lengthy "wake-up preamble" to every command.
             */
            powerMode = CmdGetPowerMode();
            try
            {
                if (M6eFamilyList.Contains(model))
                {
                    isM6eVariant = true;
                    transportType = (TransportType)Enum.Parse(typeof(TransportType), CmdGetReaderConfiguration(Configuration.CURRENT_MESSAGE_TRANSPORT).ToString(), true);
                    if (transportType.Equals(TransportType.SOURCEUSB))
                    {
                        try
                        {
                            CmdSetReaderConfiguration(Configuration.SEND_CRC, false);
                            isCRCEnabled = false;
                        }
                        catch (ReaderException)
                        {
                            //Ignoring unsupported exception with old firmware and latest API
                        }
                    }
                }
            }
            catch (ReaderCodeException)
            {
                //Not fatal, going with crc enabled
            }

            // TODO: Confirm M5e preserves serial speed going from bootloader to app
            // Some M5e firmwares reset to 9600 bps going into bootloader, some don't.
            // Not sure about the other way, so we're playing it safe for now.
            if (!(model.Equals("M3e")))
            {
                ChangeBaudRate((int)ParamGet("/reader/baudRate"));
            }
        }

        #endregion

        private void UpdateAntDetResult()
        {
            if (updateAntennaDetection)
            {
                _antdetResult = CmdAntennaDetect();
            }
            _physPortList = new List<int>();
            _connectedPhysPortList = new List<int>();
            foreach (byte[] portstat in _antdetResult)
            {
                byte port = portstat[0];
                _physPortList.Add(port);
                bool connected = (0 != portstat[1]);
                if (connected)
                {
                    _connectedPhysPortList.Add(port);
                }
            }
            _physPortList.Sort();
            _connectedPhysPortList.Sort();
        }

        private int[] ConnectedPortList
        {
            get
            {
                UpdateAntDetResult();
                return _connectedPhysPortList.ToArray();
            }
        }
        private int[] PortList
        {
            get
            {
                if (null == _antdetResult) UpdateAntDetResult();
                return _physPortList.ToArray();
            }
        }

        #region CmdGetHardwareVersion

        /// <summary>
        /// Get a block of hardware version information. This information is
        /// an opaque data block.        
        /// </summary>
        /// <param name="option">opaque option argument</param>
        /// <param name="flags">opaque flags option</param>
        /// <returns>the version block</returns>
        public byte[] CmdGetHardwareVersion(byte option, byte flags)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x10);
            cmd.Add(option);
            cmd.Add(flags);
            return (GetDataFromM5eResponse(SendM5eCommand(cmd)));
        }

        #endregion

        #region CmdGetSerialNumber

        /// <summary>
        /// Get reader serial number       
        /// </summary>
        /// <returns>The module serial number</returns>
        public string CmdGetSerialNumber()
        {
            byte[] serialNumber_byte = CmdGetHardwareVersion(0x00, 0x40);
            char[] serialNumber_char = new char[serialNumber_byte[3]];
            for (int i = 0; i < serialNumber_char.Length; i++)
                serialNumber_char[i] = (char)serialNumber_byte[i + 4];

            return new string(serialNumber_char);

        }

        #endregion

        #region CmdGetCurrentProgram
        /// <summary>
        /// Return the identity of the program currently running on the
        /// device (bootloader or application).
        /// </summary>
        /// <returns>Current Program code.</returns>
        public byte CmdGetCurrentProgram()
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x0C);
            return (GetDataFromM5eResponse(SendM5eCommand(cmd)))[0];
        }

        #endregion

        #region CmdGetTemperature
        /// <summary>
        /// Get the current temperature of the device.
        /// </summary>
        /// <returns>the temperature, in degrees C</returns>
        public sbyte CmdGetTemperature()
        {
            byte[] data = new byte[1];
            data[0] = 0x72;
            return (sbyte)GetDataFromM5eResponse(SendM5eCommand(data))[0];
        }
        #endregion

        #region CmdRaw

        /// <summary>
        ///Send a raw message to the serial reader.@throws ReaderCommException in the event of a timeout (failure to receive a complete message in the specified time) or a CRC error.Does not generate exceptions for non-zero status responses.This function not intended for general use.If you really need to use raw reader commands, see the source for further instructions.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to wait for a response</param>
        /// <param name="message">The bytes of the message to send to the reader,starting with the opcode. The message header, length, and trailing CRC are not included. The message can not be empty, or longer than 251 bytes.</param>
        /// <returns>The bytes of the response, from the opcode to the end of the message. Header, length, and CRC are not included.</returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public byte[] CmdRaw(int timeout, params byte[] message)
        {
            byte[] rawResponse = SendTimeoutUnchecked(message, timeout);
            int soflenlen = 2;
            int crclen = 2;
            byte[] response = SubArray(rawResponse, soflenlen, rawResponse.Length - soflenlen - crclen);
            return response;
        }

        #endregion

        #endregion

        #region Version Command and Utility Methods

        #region CmdVersion

        /// <summary>
        /// Get the version information about the device.
        /// </summary>
        /// <returns>the VersionInfo structure describing the device.</returns>
        public VersionInfo CmdVersion()
        {
            VersionInfo vInfo = null;
            byte[] data = new byte[] { 0x03 };
            byte[] response = default(byte[]);
            sendMessage(data, ref data[0], 0);
            do
            {
                try
                {
                    response = receiveMessage(data[0], 0);
                    vInfo = ParseVersionResponse(GetDataFromM5eResponse(response));
                    break;
                }
                catch (ReaderException re)
                {
                    if (!re.Message.Equals("Boot response received."))
                    {
                        throw re;
                    }
                }
            } while (true);

            return vInfo;
        }

        #endregion

        #region ParseVersionResponse

        /// <summary>
        /// Parse Get Version response into data structure
        /// </summary>
        /// <param name="rsp">Get Version response data bytes</param>
        /// <returns>Parsed version data structure</returns>
        private static VersionInfo ParseVersionResponse(byte[] rsp)
        {
            VersionInfo v = new VersionInfo();
            v.Bootloader = new VersionNumber(rsp[0], rsp[1], rsp[2], rsp[3]);
            v.Hardware = new VersionNumber(rsp[4], rsp[5], rsp[6], rsp[7]);
            v.FirmwareDate = new VersionNumber(rsp[8], rsp[9], rsp[10], rsp[11]);
            v.Firmware = new VersionNumber(rsp[12], rsp[13], rsp[14], rsp[15]);
            UInt32 protoMask = ByteConv.ToU32(rsp, 16);
            ArrayList numList = new ArrayList();

            for (int i = 0; i < 32; i++)
            {
                if (0 != (protoMask & (1 << i)))
                {
                    int protoNum = i + 1;
                    numList.Add(protoNum);
                }
            }

            TagProtocol[] readerProto = new TagProtocol[numList.Count];

            for (int i = 0; i < numList.Count; i++)
                readerProto[i] = TranslateProtocol((SerialTagProtocol)numList[i]);

            v.SupportedProtocols = readerProto;

            return v;
        }

        #endregion

        #region GetBootloader

        private static string GetBootloader(VersionInfo vi)
        {
            return vi.Bootloader.ToString();
        }

        #endregion

        #region GetFirmware

        private static string GetFirmware(VersionInfo vi)
        {
            return vi.Firmware.ToString();
        }

        #endregion

        #region GetFirmwareDate

        private static string GetFirmwareDate(VersionInfo vi)
        {
            return vi.FirmwareDate.ToString();
        }

        #endregion

        #region GetHardware

        private static string GetHardware(VersionInfo vi)
        {
            return vi.Hardware.ToString();
        }

        #endregion

        #region GetModel

        private void GetModel(VersionInfo vi)
        {
            switch (vi.Hardware.Part1)
            {
                case MODEL_M5E: model = "M5e"; break;
                case MODEL_M5E_COMPACT: model = "M5e Compact"; break;
                case MODEL_M5E_I:
                    switch (vi.Hardware.Part4)
                    {
                        case MODEL_M5E_I_REV_EU: model = "M5e EU"; break;
                        case MODEL_M5E_I_REV_NA: model = "M5e NA"; break;
                        case MODEL_M5E_I_REV_JP: model = "M5e JP"; break;
                        case MODEL_M5E_I_REV_PRC: model = "M5e PRC"; break;
                        default: model = "M5e EU"; break; // If there is no model recognized, it should fallback to base model.
                    }
                    break;
                case MODEL_M4E: model = "M4e"; break;
                case MODEL_M6E: model = "M6e"; break;
                case MODEL_M6E_I:
                    switch (vi.Hardware.Part4)
                    {
                        case MODEL_M6E_I_PRC: model = "M6e PRC"; break;
                        case MODEL_M6E_I_JIC: model = "M6e JIC"; break;
                        default: model = "M6e"; break; // If there is no model recognized, it should fallback to base model.
                    }
                    break;
                case MODEL_MICRO:
                    switch (vi.Hardware.Part4)
                    {
                        case MODEL_M6E_MICRO: model = "M6e Micro"; break;
                        case MODEL_M6E_MICRO_USB: model = "M6e Micro USB"; break;
                        case MODEL_M6E_MICRO_USB_PRO: model = "M6e Micro USBPro"; break;
                        default: model = "M6e Micro"; break; // If there is no model recognized, it should fallback to base model.
                    }
                    break;
                case MODEL_M6E_NANO: model = "M6e Nano"; break;
                case MODEL_M3E:
                    switch (vi.Hardware.Part4)
                    {
                        case MODEL_M3E_I_REV1: model = "M3e"; break;
                        default: model = "M3e"; break; // If there is no model recognized, it should fallback to base model.
                    }
                    break;
                default: model = "Unknown"; break;
            }
        }
        #endregion

        #region GetSoftware

        private static string GetSoftware(VersionInfo vi)
        {
            return String.Format("{0}-{1}-BL{2}", GetFirmware(vi), GetFirmwareDate(vi), GetBootloader(vi));
        }

        #endregion

        #endregion

        #region Antenna Commands

        #region GetConnectedPortDict

        /// <summary>
        /// Make set of connected ports
        /// </summary>
        /// <returns>Dictionary containing each connected port number as a key.</returns>
        private Dictionary<int, object> GetConnectedPortDict()
        {
            Dictionary<int, object> connectedPorts = new Dictionary<int, object>();

            foreach (int port in ConnectedPortList)
                connectedPorts.Add(port, null);

            return connectedPorts;
        }

        #endregion

        #region GetLogicalAntennas

        /// <summary>
        /// Get list of logical antenna numbers, as defined in TxRxMap
        /// </summary>
        /// <returns></returns>
        private int[] GetLogicalAntennas()
        {
            List<int> antennas = new List<int>();
            foreach (int[] row in _txRxMap.Map)
            {
                antennas.Add(row[0]);
            }
            return antennas.ToArray();
        }

        #endregion

        #region GetLogicalConnectedAntennas

        /// <summary>
        /// Get list of connected antennas, using logical antenna numbers
        /// </summary>
        /// <returns></returns>
        private int[] GetLogicalConnectedAntennas()
        {
            List<int> connectedAntennas = new List<int>();
            Dictionary<int, object> connectedPorts = GetConnectedPortDict();

            foreach (int[] row in _txRxMap.Map)
            {
                int ant = row[0];
                int tx = row[1];
                int rx = row[2];

                if (connectedPorts.ContainsKey(tx) && connectedPorts.ContainsKey(rx))
                    connectedAntennas.Add(ant);
            }
            //this is introduced to check logical antennas not exceed 64 
            //int cnt = connectedAntennas.Count - 1;
            int cnt = connectedAntennas.Count;
            if (cnt > LOGICAL_ANTENNAS_ALLOWED)
            {
                while(cnt > LOGICAL_ANTENNAS_ALLOWED)
                {
                    connectedAntennas.RemoveAt(cnt-1);
                    cnt--;
                }
            }
            return connectedAntennas.ToArray();
        }

        #endregion

        #region ConnectedAntennaList

        private int[] ConnectedAntennaList
        {
            get
            {
                UpdateAntDetResult();
                return GetLogicalConnectedAntennas();
            }
        }
        #endregion

        #region AntennaList
        private int[] AntennaList
        {
            get
            {
                return GetLogicalAntennas();
            }
        }
        #endregion

        #region SetLogicalAntenna

        private void SetLogicalAntenna(int ant)
        {
            if (_currentAntenna != ant)
            {
                int[] txrx = _txRxMap.GetTxRx(ant);
                CmdSetTxRxPorts(txrx[0], txrx[1]);
                _currentAntenna = ant;
            }
        }

        #endregion

        #region SetLogicalAntennaList

        private void SetLogicalAntennaList(int[] ants)
        {
            // Assume caller has already verified ants
            // to be non-null and >0 in length.
            if (false == ArrayEquals<int>(_searchList, ants))
            {
                List<byte[]> txrxlist = new List<byte[]>();
                foreach (int ant in ants)
                {
                    int[] txrx = _txRxMap.GetTxRx(ant);
                    txrxlist.Add(new byte[] { (byte)txrx[0], (byte)txrx[1] });
                }
                CmdSetAntennaSearchList(txrxlist.ToArray());
                _searchList = (int[])ants.Clone();
            }
        }

        #endregion

        #region CmdGetTxRxPorts

        /// <summary>
        /// Get the currently set Tx and Rx antenna port.
        /// </summary>
        /// <returns>a two-element array: {tx port, rx port}</returns>
        public byte[] CmdGetTxRxPorts()
        {
            byte[] data = new byte[] { 0x61, 0x00 };
            byte[] response = SendM5eCommand(data);
            return GetDataFromM5eResponse(response);
        }

        #endregion

        #region CmdGetAntennaConfiguration

        /// <summary>
        /// Get the current Tx and Rx antenna port, the number of physical
        /// ports, and a list of ports where an antenna has been detected.
        /// </summary>
        /// <returns>an object containing the antenna port information</returns>
        public AntennaPort CmdGetAntennaConfiguration()
        {
            byte[] data = new byte[] { 0x61, 0x01 };
            byte[] response = SendM5eCommand(data);
            AntennaPort ants = new AntennaPort();
            int i = ARGS_RESPONSE_OFFSET;
            ants.TxAntenna = response[i++];
            ants.RxAntenna = response[i++];
            //i += 2;
            int numAnts = response[LENGTH_RESPONSE_OFFSET] - 2;
            List<int> portlist = new List<int>();
            List<int> termlist = new List<int>();
            for (int iPort = 0; iPort < numAnts; iPort++)
            {
                bool terminated = (0x01 == response[i++]);
                byte antNum = (byte)(iPort + 1);
                portlist.Add(antNum);
                if (terminated) { termlist.Add(antNum); }
            }
            ants.PortList = portlist.ToArray();
            ants.TerminatedPortList = termlist.ToArray();
            return ants;
        }

        #endregion

        #region CmdGetAntennaSearchList
        /// <summary>
        /// Gets the search list of logical antenna ports.
        /// </summary>
        /// <returns>
        /// an array of 2-element arrays of integers interpreted as
        /// (tx port, rx port) pairs. Example, representing a monostatic
        /// antenna on port 1 and a bistatic antenna pair on ports 3 and 4:
        /// {{1, 1}, {3, 4}}
        /// </returns>
        public byte[][] CmdGetAntennaSearchList()
        {
            byte[] data = new byte[] { 0x61, 0x02 };
            byte[] result = GetDataFromM5eResponse(SendM5eCommand(data));
            byte[][] logicalAntNumAndInfo = new byte[(result.Length - 1) / 2][];
            int offset = 1;

            for (int i = 0; i < (int)((result.Length - 1) / 2); i++)
                logicalAntNumAndInfo[i] = new byte[] { result[offset++], result[offset++] };

            return logicalAntNumAndInfo;
        }
        #endregion

        #region CmdGetAntennaPortPowers
        /// <summary>
        /// Gets the transmit powers of each antenna port.
        /// </summary>
        /// <returns>Returns Antenna associated Read and Write Power
        /// an array of 3-element arrays of integers interpreted as
        /// (tx port, read power in centidBm, write power in centidBm)
        /// triples. Example, with read power levels of 30 dBm and write
        /// power levels of 25 dBm : {{1, 3000, 2500}, {2, 3000, 2500}}
        /// </returns>
        public UInt16[][] CmdGetAntennaPortPowers()
        {
            byte[] data = new byte[] { 0x61, 0x03 };
            byte[] response = SendM5eCommand(data);
            byte[] result = GetDataFromM5eResponse(response);
            UInt16[][] logicalAntNumAndInfo = new UInt16[(result.Length - 1) / 5][];
            int offset = 1;
            for (int i = 0; i < (int)((result.Length - 1) / 5); i++)
            {
                logicalAntNumAndInfo[i] = new UInt16[] { result[offset], ByteConv.ToU16(result, 1 + offset), ByteConv.ToU16(result, 3 + offset) };
                offset += 5;
            }
            return logicalAntNumAndInfo;
        }

        #endregion

        #region CmdGetAntennaPortPowersAndSettlingTime

        /// <summary>
        /// Gets the transmit powers and settling time of each antenna port.
        /// </summary>
        /// <returns>an array of 4-element arrays of integers interpreted as
        /// (tx port, read power in centidBm, write power in centidBm,
        /// settling time in microseconds) tuples.  An example with two
        /// antenna ports, read power levels of 30 dBm, write power levels of
        /// 25 dBm, and 500us settling times:
        /// {{1, 3000, 2500, 500}, {2, 3000, 2500, 500}}
        /// </returns>
        public Int16[][] CmdGetAntennaPortPowersAndSettlingTime(int column)
        {

            byte[] data = new byte[3];
            if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA) && column != 0)
            {
                data = new byte[] { 0x61, 0x04, Convert.ToByte(column) };
            }
            else
            {
                data = new byte[] { 0x61, 0x04 };
            }

            byte[] response = SendM5eCommand(data);
            byte[] result = GetDataFromM5eResponse(response);
            Int16[][] logicalAntNumAndInfo = new Int16[7][];
            int offset = 1;
            if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA))
            {
                offset++;
                logicalAntNumAndInfo = new Int16[(result.Length - 1) / 3][];
                for (int i = 0; i < (int)((result.Length - 1) / 3); i++)
                {
                    logicalAntNumAndInfo[i] = new Int16[] { result[offset], (Int16)ByteConv.ToU16(result, 1 + offset) };//, (Int16)ByteConv.ToU16(result, 3 + offset), (Int16)ByteConv.ToU16(result, 5 + offset) };
                    offset += 3;
                }
            }
            else
            {
                logicalAntNumAndInfo = new Int16[(result.Length - 1) / 7][];
                for (int i = 0; i < (int)((result.Length - 1) / 7); i++)
                {
                    logicalAntNumAndInfo[i] = new Int16[] { result[offset], (Int16)ByteConv.ToU16(result, 1 + offset), (Int16)ByteConv.ToU16(result, 3 + offset), (Int16)ByteConv.ToU16(result, 5 + offset) };
                    offset += 7;
                }
            }

            return logicalAntNumAndInfo;
        }

        #endregion

        #region CmdAntennaDetect

        /// <summary>
        /// Enumerate the logical antenna ports and report the antenna
        /// detection status of each one.
        /// </summary>
        /// <returns>
        /// an array of 2-element arrays of integers which are
        /// (logical antenna port, detected) pairs. An example, where logical
        /// ports 1 and 2 have detected antennas and 3 and 4 do not:
        /// {{1, 1}, {2, 1}, {3, 0}, {4, 0}}
        /// </returns>
        public byte[][] CmdAntennaDetect()
        {
            byte[][] logicalAntNumAndInfo = default(byte[][]);
            try
            {
                byte[] data = new byte[] { 0x61, 0x05 };
                byte[] response = SendM5eCommand(data);
                byte[] result = GetDataFromM5eResponse(response);
                logicalAntNumAndInfo = new byte[(result.Length - 1) / 2][];
                int offset = 1;
                for (int i = 0; i < (int)((result.Length - 1) / 2); i++)
                    logicalAntNumAndInfo[i] = new byte[] { result[offset++], result[offset++] };
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return logicalAntNumAndInfo;
        }

        #endregion

        #region CmdGetAntennaReturnLoss
        /// <summary>
        /// Gets the antenna return loss of logical antenna ports.
        /// </summary>
        /// <returns>
        /// an array of 2-element arrays of integers interpreted as
        /// (tx port, returnloss) pairs.
        /// </returns>
        public int[][] CmdGetAntennaReturnLoss()
        {
            byte[] data = new byte[] { 0x61, 0x06 };
            byte[] result = GetDataFromM5eResponse(SendM5eCommand(data));
            int[][] logicalAntNumAndReturnLoss = new int[(result.Length - 1) / 2][];
            int offset = 1;

            for (int i = 0; i < (int)((result.Length - 1) / 2); i++)
                logicalAntNumAndReturnLoss[i] = new int[] { result[offset++], result[offset++] };

            return logicalAntNumAndReturnLoss;
        }
        #endregion

        #region CmdSetTxRxPorts

        /// <summary>
        /// Sets the Tx and Rx antenna port. Port numbers range from 1-255.
        /// </summary>
        /// <param name="txPort">the logical antenna port to use for transmitting</param>
        /// <param name="rxPort">the logical antenna port to use for receiving</param>
        public void CmdSetTxRxPorts(int txPort, int rxPort)
        {
            byte[] data = new byte[3];
            data[0] = 0x91;
            data[1] = (byte)txPort;
            data[2] = (byte)rxPort;
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #region CmdSetAntennaSearchList

        /// <summary>
        /// Sets the search list of logical antenna ports. Port numbers range
        /// from 1-255.
        /// </summary>
        /// <param name="list">
        /// the ordered search list. An array of 2-element arrays
        /// of integers interpreted as (tx port, rx port) pairs. Example,
        /// representing a monostatic antenna on port 1 and a bistatic
        /// antenna pair on ports 3 and 4: {{1, 1}, {3, 4}}
        /// </param>
        public void CmdSetAntennaSearchList(byte[][] list)
        {
            //Fixed the bug#1775
            //If the antenna list is null or list length is zero, API throws an exception
            //instead of sending truncated 01 91 02 command to reader
            if (list == null || list.Length == 0)
            {
                throw new ArgumentException("No antennas specified.");
            }
            else
            {
                List<byte> cmd = new List<byte>();
                cmd.Add(0x91);
                cmd.Add(0x02);
                for (int i = 0; i < list.Length; i++)
                    cmd.AddRange(list[i]);

                SendM5eCommand(cmd);
            }
        }
        #endregion


        #region CmdSetAntennaReadTimeList
        /// <summary>
        /// This method sets the antenna read time in the list as per the read plans
        /// </summary>
        /// <param name="plan">Read plan</param>
        /// <param name="timeout">Time out</param>
        public void CmdSetAntennaReadTimeList(ReadPlan plan, int timeout)
        {
            int asyncOnTime, asyncOffTime;
            List<byte> cmd = new List<byte>();
            cmd.Add(0x91);
            cmd.Add(0x07);
            if (enableStreaming)
            {
                asyncOnTime = (int)ParamGet("/reader/read/asyncOnTime");
                asyncOffTime = (int)ParamGet("/reader/read/asyncOffTime");
                setAntennaReadTimeHelper(cmd, plan, asyncOnTime, asyncOffTime);
            }
            else
            {
                setAntennaReadTimeHelper(cmd, plan, timeout, 0);
            }
            SendM5eCommand(cmd);
        }
        #endregion

        #region CmdSetAntennaPortPowers

        /// <summary>
        /// Sets the transmit powers of each antenna port. Note that setting
        /// a power level to 0 will cause the corresponding global power
        /// level to be used. Port numbers range from 1-255; power levels
        /// range from 0-65535.
        /// </summary>
        /// <param name="list">
        /// an array of 3-element arrays of integers interpreted as
        /// (tx port, read power in centidBm, write power in centidBm)
        /// triples. Example, with read power levels of 30 dBm and write
        /// power levels of 25 dBm : {{1, 3000, 2500}, {2, 3000, 2500}}
        /// </param>
        public void CmdSetAntennaPortPowers(UInt16[][] list)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x91);
            cmd.Add(0x03);

            for (int i = 0; i < (list.Length); i++)
            {
                cmd.Add((byte)list[i][0]);
                for (int j = 1; j < list[i].Length; j++)
                    cmd.AddRange(ByteConv.EncodeU16(list[i][j]));
            }

            SendM5eCommand(cmd);
        }
        #endregion

        #region CmdSetAntennaPortPowersAndSettlingTime

        /// <summary>
        /// Sets the transmit powers and settling times of each antenna
        /// port. Note that setting a power level to 0 will cause the
        /// corresponding global power level to be used. Port numbers range
        /// from 1-255; power levels range from 0-65535; settling time ranges
        /// from 0-65535.
        /// </summary>
        /// <param name="list">
        /// an array of 4-element arrays of integers interpreted as
        /// (tx port, read power in centidBm, write power in centidBm,
        /// settling time in microseconds) tuples.  An example with two
        /// antenna ports, read power levels of 30 dBm, write power levels of
        /// 25 dBm, and 500us settling times:
        /// {{1, 3000, 2500, 500}, {2, 3000, 2500, 500}}
        /// </param>
        /// <param name="column">read, write and settling time 
        /// </param>
        public void CmdSetAntennaPortPowersAndSettlingTime(Int16[][] list, int column)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x91);
            cmd.Add(0x04);
            if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA))
            {
                cmd.Add(Convert.ToByte(column));
            }

            for (int i = 0; i < (list.Length); i++)
            {
                cmd.Add((byte)list[i][0]);

                for (int j = 1; j < list[i].Length; j++)
                    cmd.AddRange(ByteConv.EncodeS16(list[i][j]));
            }


            SendM5eCommand(cmd);
        }

        #endregion

        #region setAntennaReadTimeHelper
        /// <summary>
        /// This method recursively assemble a setAntennaReadTime command
        /// </summary>
        /// <param name="cmd">Framed Message Content</param>
        /// <param name="plan">Read Plan</param>
        /// <param name="onTime">onTime</param>
        /// <param name="offTime">offTime</param>
        public void setAntennaReadTimeHelper(List<byte> cmd, ReadPlan plan, int onTime, int offTime)
        {
            int subOnTime = 0, subOffTime = 0;
            int j, antennaCount;
            if (plan is SimpleReadPlan)
            {
                SimpleReadPlan srp = (SimpleReadPlan)plan;

                //Find out the exact number of valid antennas from the provided antenna list.
                for (j = 0, antennaCount = 0; j < srp.Antennas.Length; j++)
                {
                    if (srp.Antennas[j] != 0)
                    {
                        antennaCount++;
                    }
                }
                //Throw an error in case of invalid antenna.
                if (antennaCount != srp.Antennas.Length)
                {
                    throw new Exception("Invalid argument");
                }

                if (antennaCount != 0)
                {
                    //Divide Global asyncOn time by number of antennas.
                    subOnTime = onTime / antennaCount;

                    //If asyncOfff time is non-zero, devide it by number of antennas.
                    if ((enableStreaming) && (offTime != 0))
                    {
                        subOffTime = offTime / antennaCount;
                    }
                }

                // Embedding ontime and offtime for the antenna list in "per antenna ontime"(91 07) command.
                for (j = 0; j < srp.Antennas.Length; j++)
                {
                    if (srp.Antennas[j] != 0)
                    {
                        int[] txrx = _txRxMap.GetTxRx(srp.Antennas[j]); //gets logical tx and rx ports
                        cmd.Add((byte)txrx[0]); // set logical tx port
                        cmd.AddRange(ByteConv.EncodeU16((ushort)subOnTime));

                        //If subOfftime is non-zero, follow it immediately after ontime with 0x00 as an antenna number.
                        if (enableStreaming)
                        {
                            int asyncOffTime = (int)ParamGet("/reader/read/asyncOffTime");
                            if (asyncOffTime != 0)
                            {
                                cmd.Add(0x00);
                                cmd.AddRange(ByteConv.EncodeU16((ushort)subOffTime));
                            }
                        }
                    }
                }
            }
            else if (plan is MultiReadPlan)
            {
                MultiReadPlan mrp = (MultiReadPlan)plan;
                for (j = 0; j < mrp.Plans.Length; j++)
                {
                    ReadPlan subPlan = mrp.Plans[j];
                    if (mrp.TotalWeight != 0)
                    {
                        subOnTime = (subPlan.Weight * onTime) / mrp.TotalWeight;
                        subOffTime = (subPlan.Weight * offTime) / mrp.TotalWeight;
                    }
                    else
                    {
                        subOnTime = onTime / mrp.Plans.Length;
                        subOffTime = offTime / mrp.Plans.Length;
                    }
                    setAntennaReadTimeHelper(cmd, subPlan, subOnTime, subOffTime);
                }
            }
        }
        #endregion

        #region ValidateAntenna

        /// <summary>
        /// Is requested antenna a valid antenna?
        /// </summary>
        /// <param name="reqAnt">Requested antenna</param>
        /// <returns>reqAnt if it is in the set of valid antennas, else throws ArgumentException</returns>
        private int ValidateAntenna(int reqAnt)
        {
            int a = ValidateParameter<int>(reqAnt, _txRxMap.ValidAntennas, "Invalid antenna");
            SetLogicalAntenna(a);
            return a;
        }

        #endregion

        #endregion

        #region PSAM

        #region CmdAuthReqResponse

        /// <summary>
        /// Send new message to tag with secure accesspassword corresponding with tag epc 
        /// </summary>
        /// <param name="password">secure accesspassword to read tag data</param>
        public void CmdAuthReqResponse(UInt32 password)
        {
            List<byte> cmd = new List<byte>();
            // Opcode
            cmd.Add(0X2F);
            // Command timeout
            cmd.AddRange(ByteConv.EncodeU16(0));
            // Sub cmd
            cmd.Add(0x03);
            // Options
            cmd.Add(0x00);
            cmd.Add(0x01);
            // Data length
            cmd.Add(0x00);
            cmd.Add(0x20);
            // Password
            cmd.AddRange(ByteConv.EncodeU32(password));
            sendMessage(cmd, ref opCode, 0);
        }

        #endregion

        #endregion

        #region Region Commands and Utility Methods

        #region CmdGetAvailableRegions

        /// <summary>
        /// Get the list of regulatory regions supported by the device.
        /// </summary>
        /// <returns>an array of supported regions</returns>
        public Region[] CmdGetAvailableRegions()
        {
            SerialRegion[] SerialRegions = ParseSerialRegions(GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x71 })));
            Region[] regions = new Region[SerialRegions.Length];

            for (int i = 0; i < SerialRegions.Length; i++)
                regions[i] = RegionToTM(SerialRegions[i]);

            return regions;
        }

        #endregion

        #region CmdGetRegion

        /// <summary>
        /// Gets the current region the device is configured to use.
        /// </summary>
        /// <returns>the region</returns>
        public Region CmdGetRegion()
        {
            return ParseRegion(GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x67 }))[0]);
        }

        #endregion

        #region CmdSetRegion

        /// <summary>
        /// Set the current regulatory region for the device. Resets region-specific 
        /// configuration, such as the frequency hop table.
        /// </summary>
        /// <param name="region">the region to set</param>        
        public void CmdSetRegion(Region region)
        {
            byte[] data = new byte[] { 0x97, RegionToCode(region) };
            SendM5eCommand(data);
        }
        
        ///// <summary>
        ///// Custom region Configuration
        ///// </summary>
        ///// <param name="LBTEnable"></param>
        ///// <param name="LBTThreshold"></param>
        ///// <param name="dwellTimeEnable"></param>
        ///// <param name="dwellTime"></param>
        //public override void regionConfiguration(bool LBTEnable, Int16 LBTThreshold, bool dwellTimeEnable, UInt16 dwellTime)
        //{
        //    if (IsRegionConfgurationSupported())
        //    {
        //        this.regionConfigurationFlag = true;
        //        try
        //        {
        //            if (LBTEnable)
        //            {
        //                this.LBTEnable = true;
        //            }
        //            else
        //            {
        //                this.LBTEnable = false;
        //            }
        //            if (dwellTimeEnable)
        //            {
        //                this.dwellTimeEnable = true;
        //            }
        //            else
        //            {
        //                this.dwellTimeEnable = false;
        //            }
        //            this.dwellTime = dwellTime;
        //            this.LBTThreshold = LBTThreshold;
        //        }
        //        catch (Exception ex)
        //        {
        //            throw ex;
        //        }
        //    }
        //    else
        //    {
        //        throw new ReaderException("Operation not supported");
        //    }
        //}
        #endregion

        #region CmdSetRegionLbt

        /// <summary>
        /// Set the current regulatory region for the device.
        /// Resets region-specific configuration, such as the frequency hop table.
        /// </summary>
        /// <param name="region">Region code</param>        
        /// <param name="lbt">Enable LBT?</param>
        public void CmdSetRegionLbt(Region region, bool lbt)
        {
            byte[] data = new byte[] { 0x97, 0x01, RegionToCode(region), 0x40, BoolToByte(lbt) };
            SendM5eCommand(data);
        }

        /// <summary>
        /// set LBT threshold value
        /// </summary>
        /// <param name="region"></param>
        /// <param name="rep"></param>
        public void CmdSetLBTThreshold(Region region, Int16 rep)
        {
            byte[] data = new byte[5];
            data[0] = 0x97;
            data[1] = 0x01;
            data[2] = RegionToCode(region);
            data[3] = 0x41;
            data[4] = (byte)rep;
            SendM5eCommand(data);
        }

        /// <summary>
        /// set dwell time enable
        /// </summary>
        /// <param name="region"></param>
        /// <param name="rep"></param>
        public void CmdSetDwellEnable(Region region, bool rep)
        {
            byte[] data = new byte[5];
            data[0] = 0x97;
            data[1] = 0x01;
            data[2] = RegionToCode(region);
            data[3] = 0x42;
            data[4] = BoolToByte(rep);
            SendM5eCommand(data);
        }

       /// <summary>
       /// set dwell time 
       /// </summary>
       /// <param name="region"></param>
       /// <param name="rep"></param>
        public void CmdSetDwellTime(Region region, UInt16 rep)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x97);
            cmd.Add(0x01);
            cmd.Add(RegionToCode(region));
            cmd.Add(0x43);
            cmd.AddRange(ByteConv.EncodeU16(rep));
            SendM5eCommand(cmd);
        }

        #endregion

        #region CmdGetRegionConfiguration

        /// <summary>
        /// Get the value of a region-specific configuration setting.
        /// </summary>
        /// <param name="key">the setting</param>
        /// <returns>
        /// Object with the setting value. The type of the object depends on the key.
        /// See the RegionConfiguration class for details.
        /// </returns>
        public Object CmdGetRegionConfiguration(RegionConfiguration key)
        {
            return ParseRegionConfiguration(key,
                GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x67, 0x01, (byte)key })));
        }
        #endregion

        #region ParseRegionConfiguration

        private static Object ParseRegionConfiguration(RegionConfiguration key, byte[] rsp)
        {
            switch (key)
            {
                default:
                    return null;
                case RegionConfiguration.LBTENABLED:
                    return rsp[3] == 1;
                case RegionConfiguration.LBTTHRESHOLD:
                    return unchecked(ConvertTo2Complement(rsp[3]));
                case RegionConfiguration.DWELLENABLE:
                    return rsp[3] == 1;
                case RegionConfiguration.DWELLTIME:
                    int len = rsp.Length;
                    byte[] b1 = new byte[rsp.Length - 3];
                    Array.Copy(rsp, 3, b1, 0, 2);
                    Array.Reverse(b1);
                    return BitConverter.ToUInt16(b1, 0);
            }
        }

        private static sbyte ConvertTo2Complement(byte b)
        {
            if (b < 128)
            {
                return Convert.ToSByte(b);
            }
            else
            {
                int x = Convert.ToInt32(b);
                return Convert.ToSByte(x - 256);
            }
        }

        #endregion

        #region ParseRegion
        /// <summary>
        /// Convert byte to region code
        /// </summary>
        /// <param name="b"></param>
        /// <returns>Region code</returns>
        private static Region ParseRegion(byte b)
        {
            SerialRegion sr = ParseSerialRegion(b);
            Region tmr = RegionToTM(sr);
            return tmr;
        }
        #endregion

        #region RegionToCode

        private static byte RegionToCode(Region region)
        {
            return (byte)RegionToM5e(region);
        }

        #endregion

        #region ParseSerialRegions

        /// <summary>
        /// Convert array of bytes into list of region codes
        /// </summary>
        /// <param name="bs">Array of bytes representing region codes</param>
        /// <returns>List of regions</returns>
        private static SerialRegion[] ParseSerialRegions(byte[] bs)
        {
            List<SerialRegion> rs = new List<SerialRegion>();
            for (int i = 0; i < bs.Length; i++)
            {
                try
                {
                    rs.Add(ParseSerialRegion(bs[i]));
                }
                catch (ReaderParseException)
                {
                }
            }
            return rs.ToArray();
        }

        #endregion

        #region ParseSerialRegion

        // TODO: Consolidate with other region-parsing methods
        private static SerialRegion ParseSerialRegion(byte b)
        {
            switch (b)
            {
                case 0x00:
                    return SerialRegion.NONE;
                case 0x01:
                    return SerialRegion.NA;
                case 0x02:
                    return SerialRegion.EU;
                case 0x03:
                    return SerialRegion.KR;
                case 0x04:
                    return SerialRegion.IN;
                case 0x05:
                    return SerialRegion.JP;
                case 0x06:
                    return SerialRegion.PRC;
                case 0x07:
                    return SerialRegion.EU2;
                case 0x08:
                    return SerialRegion.EU3;
                case 0x09:
                    return SerialRegion.KR2;
                case 0x0A:
                    return SerialRegion.PRC2;
                case 0x0b:
                    return SerialRegion.AU;
                case 0x0c:
                    return SerialRegion.NZ;
                case 0x0D:
                    return SerialRegion.NA2;
                case 0x0e:
                    return SerialRegion.NA3;
                case 0x0f:
                    return SerialRegion.IS;
                case 0x10:
                    return SerialRegion.MY;
                case 0x11:
                    return SerialRegion.ID;
                case 0x12:
                    return SerialRegion.PH;
                case 0x13:
                    return SerialRegion.TW;
                case 0x14:
                    return SerialRegion.MO;
                case 0x15:
                    return SerialRegion.RU;
                case 0x16:
                    return SerialRegion.SG;
                case 0x17:
                    return SerialRegion.JP2;
                case 0x18:
                    return SerialRegion.JP3;
                case 0x19:
                    return SerialRegion.VN;
                case 0x1A:
                    return SerialRegion.TH;
                case 0x1B:
                    return SerialRegion.AR;
                case 0x1C:
                    return SerialRegion.HK;
                case 0x1D:
                    return SerialRegion.BD;
                case 0x1E:
                    return SerialRegion.EU4;
                case 0x1F:
                    return SerialRegion.UNIVERSAL;
                case 0x20:
                    return SerialRegion.IS2;
                case 0x21:
                    return SerialRegion.NA4;
                case 0xFE:
                    return SerialRegion.OPEN_EXTENDED;
                case 0xFF:
                    return SerialRegion.OPEN;
                default:
                    throw new ReaderParseException("Unrecognized region code 0x" + b.ToString("X2"));
            }
        }
        #endregion

        #region RegionToM5e
        /// <summary>
        /// Convert Region to M5eLibrary.Region
        /// </summary>
        /// <param name="tmreg">Region</param>
        /// <returns>M5eLibrary.Region</returns>
        private static SerialRegion RegionToM5e(Region tmreg)
        {
            if (_mapTmToM5eRegion.ContainsKey(tmreg))
                return _mapTmToM5eRegion[tmreg];

            throw new ReaderParseException("Unrecognized M5eLibrary.Region: " + tmreg.ToString());
        }
        #endregion

        #region RegionToTM

        /// <summary>
        /// Convert M5eLibrary.Region to Region
        /// </summary>
        /// <param name="m5ereg">M5eLibrary.Region</param>
        /// <returns>Region</returns>
        private static Region RegionToTM(SerialRegion m5ereg)
        {
            if (_mapM5eToTmRegion.ContainsKey(m5ereg))
                return _mapM5eToTmRegion[m5ereg];

            throw new ReaderParseException("Unrecognized M5eLibrary.Region: " + m5ereg.ToString());
        }

        #endregion

        #region RegionsToTM

        /// <summary>
        /// Convert M5eLibrary.Region[] to Region[]
        /// </summary>
        /// <param name="inreg">M5eLibrary.Region</param>
        /// <returns>Region</returns>
        private static Region[] RegionsToTM(SerialRegion[] inreg)
        {
            Region[] outreg = new Region[inreg.Length];

            for (int i = 0; i < inreg.Length; i++)
                outreg[i] = RegionToTM(inreg[i]);

            return outreg;
        }

        #endregion

        #endregion

        #region Power Commands

        #region CmdGetReadTxPower

        /// <summary>
        /// Get the current global Tx power setting for read operations.
        /// </summary>
        /// <returns>the power setting, in centidBm</returns>
        public Int16 CmdGetReadTxPower()
        {
            byte[] rsp = SendM5eCommand(new byte[] { 0x62, 0x00 });
            return (Int16)ByteConv.ToU16(GetDataFromM5eResponse(rsp), 1);
        }

        #endregion

        #region CmdGetReadTxPowerWithLimits

        /// <summary>
        /// Get the current global Tx power setting for read operations, and the
        /// minimum and maximum power levels supported.
        /// </summary>
        /// <returns>
        /// a three-element array: {tx power setting in centidBm,
        /// maximum power, minimum power}. Example: {2500, 3000, 500}
        /// </returns>
        public Int16[] CmdGetReadTxPowerWithLimits()
        {
            byte[] rsp = GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x62, 0x01 }));
            int count = (rsp.Length - 1) / 2;
            Int16[] powerWithLimits = new Int16[count];
            int offset = 1;
            for (int i = 0; i < count; i++)
            {
                powerWithLimits[i] = (Int16)ByteConv.ToU16(rsp, offset);
                offset += 2;
            }
            return powerWithLimits;
        }
        #endregion

        #region CmdSetReadTxPower

        /// <summary>
        /// Set the current global Tx power setting for read operations.
        /// </summary>
        /// <param name="power">the power level</param>
        public void CmdSetReadTxPower(Int16 power)
        {
            byte[] data = new byte[3];
            data[0] = 0x92;
            ByteConv.FromS16(data, 1, power);
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #region CmdGetWriteTxPower

        /// <summary>
        /// Get the current global Tx power setting for write operations.
        /// </summary>
        /// <returns>the power setting, in centidBm</returns>
        public Int16 CmdGetWriteTxPower()
        {
            byte[] rsp = SendM5eCommand(new byte[] { 0x64, 0x00 });
            return (Int16)ByteConv.ToU16(GetDataFromM5eResponse(rsp), 1);
        }

        #endregion

        #region CmdGetWriteTxPowerWithLimits

        /// <summary>
        /// Get the current global Tx power setting for write operations, and the
        /// minimum and maximum power levels supported.
        /// </summary>
        /// <returns>
        /// a three-element array: {tx power setting in centidBm,
        /// maximum power, minimum power}. Example: {2500, 3000, 500}
        /// </returns>
        public Int16[] CmdGetWriteTxPowerWithLimits()
        {
            byte[] rsp = GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x64, 0x01 }));
            Int16[] powerWithLimits = new Int16[rsp.Length / 2];
            int offset = 1;

            for (int i = 0; i < (rsp.Length - 1) / 2; i++)
            {
                powerWithLimits[i] = (Int16)ByteConv.ToU16(rsp, offset);
                offset += 2;
            }

            return powerWithLimits;
        }

        #endregion

        #region CmdSetWriteTxPower

        /// <summary>
        /// Set the current global Tx power setting for write operations.
        /// </summary>
        /// <param name="power">the power level.</param>
        public void CmdSetWriteTxPower(Int16 power)
        {
            byte[] data = new byte[3];
            data[0] = 0x94;
            ByteConv.FromS16(data, 1, power);
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #endregion

        #region Power Mode Commands and Utility Methods

        #region CmdGetPowerMode

        /// <summary>
        /// Gets the current power mode of the device.
        /// </summary>
        /// <returns>the power mode</returns>
        public PowerMode CmdGetPowerMode()
        {
            return ParsePowerMode(GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x68 }))[0]);
        }

        #endregion

        #region CmdSetPowerMode

        /// <summary>
        /// Set the current power mode of the device.
        /// </summary>
        /// <param name="powermode">the mode to set</param>
        public void CmdSetPowerMode(PowerMode powermode)
        {
            byte[] data = new byte[2];
            data[0] = 0x98;
            data[1] = (byte)powermode;
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #region ParsePowerMode

        /// <summary>
        /// Parses byte value returned by the m5e to a valid PowerMode
        /// </summary>
        /// <param name="b">Byte value of Power Mode</param>
        /// <returns>Valid Power Mode</returns>
        private static PowerMode ParsePowerMode(byte b)
        {
            switch (b)
            {
                case 0x00:
                    return (PowerMode.FULL);
                case 0x01:
                    return (PowerMode.MINSAVE);
                case 0x02:
                    return (PowerMode.MEDSAVE);
                case 0x03:
                    return (PowerMode.MAXSAVE);
                case 0x04:
                    return (PowerMode.SLEEP);
                default:
                    throw new ReaderParseException("Unrecognized Power Mode 0x" + b.ToString("X2"));
            }
        }
        #endregion

        #endregion

        #region User Mode Commands and Utility Methods

        #region CmdGetUserMode

        /// <summary>
        /// Gets the current user mode of the device.
        /// </summary>
        /// <returns>the user mode</returns>
        public UserMode CmdGetUserMode()
        {
            return ParseUserMode(GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x69 }))[0]);
        }

        #endregion

        #region CmdSetUserMode

        /// <summary>
        /// Set the current user mode of the device.
        /// </summary>
        /// <param name="usermode">the mode to set</param>
        public void CmdSetUserMode(UserMode usermode)
        {
            byte[] data = new byte[2];
            data[0] = 0x99;
            data[1] = (byte)usermode;
            byte[] inputBuffer = SendM5eCommand(data);
        }

        #endregion

        #region ParseUserMode
        /// <summary>
        /// Parses byte value returned by the M5e to a valid User Mode.
        /// </summary>
        /// <param name="b">Byte value of the User Mode</param>
        /// <returns>Valid User Mode</returns>
        private UserMode ParseUserMode(byte b)
        {
            switch (b)
            {
                case 0x00:
                    return (UserMode.NONE);
                case 0x01:
                    return (UserMode.PRINTER);
                case 0x03:
                    return (UserMode.PORTAL);
                default:
                    throw new ReaderParseException("Unrecognized User Mode 0x" + b.ToString("X2"));
            }
        }
        #endregion

        #endregion

        #region GEN2 Target Commands

        #region GetTarget

        /// <summary>
        /// Get Gen2 Target
        /// </summary>
        /// <returns>Target Value</returns>
        private Gen2.Target GetTarget()
        {
            byte[] rspdat = GetDataFromM5eResponse(SendM5eCommand(new byte[] { 0x6B, 0x05, 0x01 }));

            switch (rspdat[2])
            {
                case 0x00:
                    switch (rspdat[3])
                    {
                        case 0x00:
                            return Gen2.Target.AB;
                        case 0x01:
                            return Gen2.Target.BA;
                    }
                    break;

                case 0x01:
                    switch (rspdat[3])
                    {
                        case 0x00:
                            return Gen2.Target.A;
                        case 0x01:
                            return Gen2.Target.B;
                    }
                    break;
            }
            throw new ReaderParseException(String.Format("Unrecognized Gen2 Target Value: {0:X2} {1:X2}, rspdat[0], rspdat[1]"));
        }
        #endregion

        #region SetTarget

        /// <summary>
        /// Set Gen2 Target
        /// </summary>
        /// <param name="value">Target to set</param>
        private void SetTarget(Gen2.Target value)
        {
            byte[] cmd = new byte[] { 0x9B, 0x05, 0x01, 0xFF, 0xFF };
            byte[] opts = null;

            switch (value)
            {
                case Gen2.Target.A:
                    opts = new byte[] { 1, 0 };
                    break;
                case Gen2.Target.B:
                    opts = new byte[] { 1, 1 };
                    break;
                case Gen2.Target.AB:
                    opts = new byte[] { 0, 0 };
                    break;
                case Gen2.Target.BA:
                    opts = new byte[] { 0, 1 };
                    break;
                default:
                    throw new ArgumentException("Unrecognized Target: " + value.ToString());
            }

            Array.Copy(opts, 0, cmd, 3, opts.Length);
            SendM5eCommand(cmd);
        }
        #endregion

        #endregion

        #region Reader Configuration and Utility Methods

        #region CmdGetReaderConfiguration

        /// <summary>
        /// Gets the value of a device configuration setting.
        /// </summary>
        /// <param name="key">the setting</param>
        /// <returns>
        /// an object with the setting value. The type of the object
        /// is dependant on the key; see the Configuration class for details.
        /// </returns>
        private object CmdGetReaderConfiguration(Configuration key)
        {
            byte[] cmd = new byte[] { 0x6A, 0x01, (byte)key };
            byte[] response = SendM5eCommand(cmd);
            byte[] data = GetDataFromM5eResponse(response);
            byte[] valueBytes = new byte[data.Length - 2];
            Array.Copy(data, 2, valueBytes, 0, valueBytes.Length);
            object value = ParseReaderConfigurationValueToObject(key, valueBytes);
            return value;
        }

        #endregion

        #region CmdSetReaderConfiguration

        /// <summary>
        /// Sets the value of a device configuration setting.
        /// </summary>
        /// <param name="key">the setting</param>
        /// <param name="value">
        /// an object with the setting value. The type of the object
        /// is dependant on the key; see the Configuration class for details.
        /// </param>
        public void CmdSetReaderConfiguration(Configuration key, object value)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x9A);
            cmd.Add(0x01);
            cmd.Add((byte)key);
            if (key == Configuration.TAG_BUFFER_ENTRY_TIMEOUT)
            {
                cmd.AddRange(ByteConv.EncodeU32((uint)(int)value));
            }
            else
            {
                cmd.Add(ParseReaderConfigurationValueToByte(key, value));
            }
            SendM5eCommand(cmd);
        }

        #endregion

        #region CmdSetProtocolLicenseKey
        /// <summary>
        /// set protocol license key
        /// </summary>
        /// <param name="key">the license key</param>
        /// <returns> the supported protocol bit mask</returns>
        public List<TagProtocol> CmdSetProtocolLicenseKey(ICollection<byte> key)
        {
            return CmdSetProtocolLicenseKey(key, LicenseOption.SET_LICENSE_KEY);
        }

        /// <summary>
        /// set protocol license key
        /// </summary>
        /// <param name="key">the license key</param>        
        /// <param name="lkOption">Set/erase license key</param>
        /// <returns> the supported protocol bit mask</returns>
        public List<TagProtocol> CmdSetProtocolLicenseKey(ICollection<byte> key, LicenseOption lkOption)
        {
            byte[] response;
            List<TagProtocol> protocols = new List<TagProtocol>();
            List<byte> cmd = new List<byte>();
            cmd.Add(0x9E);
            cmd.Add((byte)lkOption);
            if (lkOption.Equals(LicenseOption.SET_LICENSE_KEY))
            {
                cmd.AddRange(key);
                byte[] resp = SendM5eCommand(cmd);
                //Capture the length of data from the response.
                int respLen = resp[1];
                if (respLen>0)
                {
                    //Parse the command response only if response length is more than 0
                    response = GetDataFromM5eResponse(resp);
                    response = SubArray(response, 1, response.Length - 1);
                    for (int i = 0; i < response.Length; i += 2)
                    {
                        protocols.Add(TranslateProtocol((SerialTagProtocol)ByteConv.ToU16(response, i)));
                    }
                }
            }
            else
            {
                response = GetDataFromM5eResponse(SendM5eCommand(cmd));
            }
            return protocols;

        }

        #endregion

        #region ParseReaderConfigurationValueToObject

        /// <summary>
        /// Parses the byte value of the reader configuration value.
        /// </summary>
        /// <param name="key">Key whose corresponding value is to be found.</param>
        /// <param name="b">Byte value of the corresponding Value</param>
        /// <returns>Bool or Byte value of the corresponding Value</returns>
        private object ParseReaderConfigurationValueToObject(Configuration key, byte b)
        {
            return ParseReaderConfigurationValueToObject(key, new byte[] { b });
        }

        /// <summary>
        /// Parse multi-byte reader configuration value
        /// </summary>
        /// <param name="key">Key whose corresponding value is to be found.</param>
        /// <param name="b">Byte value of the corresponding Value</param>
        /// <returns>Parsed value, with key-specific type</returns>
        private object ParseReaderConfigurationValueToObject(Configuration key, byte[] b)
        {
            switch (key)
            {
                case Configuration.UNIQUE_BY_ANTENNA:
                case Configuration.UNIQUE_BY_DATA:
                    return (!ByteToBool(b[0]));
                case Configuration.TRANSMIT_POWER_SAVE:
                    if (model.Equals("M6e Micro"))
                    {
                        if (b[0] == 2)
                        {
                            //Open loop power calibration
                            return true;
                        }
                        else
                        {
                            //Closed loop power calibration
                            return false;
                        }
                    }
                    else
                    {
                        return (ByteToBool(b[0]));
                    }
                case Configuration.EXTENDED_EPC:
                case Configuration.SAFETY_ANTENNA_CHECK:
                case Configuration.SAFETY_TEMPERATURE_CHECK:
                case Configuration.RECORD_HIGHEST_RSSI:
                case Configuration.RSSI_IN_DBM:
                case Configuration.ENABLE_FILTERING:
                case Configuration.UNIQUE_BY_PROTOCOL:
                case Configuration.SELF_JAMMER_CANCELLATION:
                case Configuration.CONFIGURATION_KEEP_RF_ON:
                    return (ByteToBool(b[0]));
                case Configuration.ANTENNA_CONTROL_GPIO:
                case Configuration.CURRENT_MESSAGE_TRANSPORT:
                    return b[0];
                case Configuration.PRODUCT_ID:
                case Configuration.PRODUCT_GROUP_ID:
                    {
                        UInt16 value = 0;
                        foreach (byte bn in b)
                        {
                            value <<= 8;
                            value |= (UInt16)bn;
                        }
                        return value;
                    }
                case Configuration.TAG_BUFFER_ENTRY_TIMEOUT:
                    return ByteConv.ToU32(b, 0);
                case Configuration.CONFIGURATION_TRIGGER_READ_GPIO:
                    {
                        int val = Convert.ToInt16(b[0]);
                        int[] rv;
                        if (val == 0)
                        {
                            rv = new int[] { };
                        }
                        else if ((1 <= val) && (val <= 8))
                        {
                            int i;
                            for (i = 0; i < 32; i++)
                            {
                                if ((val & (1 << i)) != 0)
                                {
                                    break;
                                }
                            }
                            rv = new int[] { i + 1 };
                        }
                        else
                        {
                            throw new ReaderParseException("Unknown response to config request");
                        }
                        return rv;
                    }
                default:
                    throw new ReaderParseException("Unrecognized Reader Configuration Key " + key.ToString());
            }
        }
        #endregion

        #region ParseReaderConfigurationValueToByte

        /// <summary>
        /// Parses the object value of the reader configuration value.
        /// </summary>
        /// <param name="key">Key whose corresponding value is to be found.</param>
        /// <param name="b">Bool or Byte value of the corresponding Value</param>
        /// <returns>Byte value of the corresponding Value</returns>
        private byte ParseReaderConfigurationValueToByte(Configuration key, object b)
        {
            switch (key)
            {
                case Configuration.UNIQUE_BY_ANTENNA:
                case Configuration.UNIQUE_BY_DATA:
                    return BoolToByte(!((bool)b));
                case Configuration.TRANSMIT_POWER_SAVE:
                    if (model.Equals("M6e Micro"))
                    {
                        if ((bool)b == true)
                        {
                            // Open loop power calibration
                            return 0x02;
                        }
                        else
                        {
                            // Closed loop power calibration
                            return 0x01;
                        }
                    }
                    else
                    {
                        // Closed loop power calibration
                        return BoolToByte((bool)b);
                    }
                case Configuration.EXTENDED_EPC:
                case Configuration.SAFETY_ANTENNA_CHECK:
                case Configuration.SAFETY_TEMPERATURE_CHECK:
                case Configuration.RECORD_HIGHEST_RSSI:
                case Configuration.RSSI_IN_DBM:
                case Configuration.ENABLE_FILTERING:
                case Configuration.UNIQUE_BY_PROTOCOL:
                case Configuration.SELF_JAMMER_CANCELLATION:
                case Configuration.SEND_CRC:
                case Configuration.CONFIGURATION_KEEP_RF_ON:
                    return BoolToByte((bool)b);
                case Configuration.ANTENNA_CONTROL_GPIO:
                    return (byte)b;
                case Configuration.CONFIGURATION_TRIGGER_READ_GPIO:
                    return Convert.ToByte(b);
                default:
                    throw new ReaderParseException("Unrecognized Reader Configuration Key " + key.ToString());
            }
        }
        #endregion

        #region CmdSetUserProfile

        /// <summary>
        /// Save/Restore/Verify/Clear the configurations.
        /// </summary>
        /// <param name="option">the operation option</param>
        /// <param name="key">the setting</param>
        /// <param name="val">the value</param>
        public void CmdSetUserProfile(UserConfigOperation option, UserConfigCategory key, UserConfigType val)
        {
            // byte[] cmd = new byte[] { 0x9D, (byte)option, (byte)key, (byte)val };
            //SendM5eCommand(cmd);
            List<byte> cmd = new List<byte>();
            List<byte> cmdMsgMultiple = new List<byte>();
            cmd.Add(0x9D);
            cmd.Add((byte)option);
            cmd.Add((byte)key);
            cmd.Add((byte)val);
            ReadPlan rp = (ReadPlan)ParamGet("/reader/read/plan");
            if (option == UserConfigOperation.CLEAR)
            {
                rp.enableAutonomousRead = false;
                ReadPlan plan;
                if (!model.Equals("M3e"))
                {
                    plan = new SimpleReadPlan(null, TagProtocol.GEN2);
                }
                else
                {
                    plan = new SimpleReadPlan(null, TagProtocol.ISO14443A);
                }
                ParamSet("/reader/read/plan", plan);
            }
            if (option == UserConfigOperation.SAVEWITHREADPLAN)
            {
                enableStreaming = true;
                if (rp.enableAutonomousRead)
                {
                    cmd.Add(0x01);
                }
                else
                {
                    cmd.Add(0x00);
                }
                userMeta = (TagMetadataFlag)ParamGet("/reader/metadata");
                if (enableStreaming)
                {
                    /*Disable filtering incase of streaming*/
                    CmdSetReaderConfiguration(Configuration.ENABLE_FILTERING, false);
                }
                UInt16 timeout = (UInt16)(int)ParamGet("/reader/read/asyncOnTime");
                List<SimpleReadPlan> readPlanList = new List<SimpleReadPlan>();
                if (rp is MultiReadPlan)
                {
                    MultiReadPlan mrp = (MultiReadPlan)rp;
                    foreach (ReadPlan r in mrp.Plans)
                    {
                        SimpleReadPlan srp = (SimpleReadPlan)r;
                        readPlanList.Add(srp);
                    }

                    if ((((MODEL_M6E == _version.Hardware.Part1) ||
                        (MODEL_M6E_I == _version.Hardware.Part1) ||
                        (MODEL_MICRO == _version.Hardware.Part1) ||
                        (MODEL_M6E_NANO == _version.Hardware.Part1) ||
                        (MODEL_M3E == _version.Hardware.Part1)) ||
                        enableStreaming) && CompareAntennas(mrp.Plans))
                    {
                        // True continuous read, if there's a consistent antenna list
                        // across the entire set of read plans.

                        AntennaSelection antsel = PrepForSearch((SimpleReadPlan)mrp.Plans[0], timeout);
                        cmdMsgMultiple = msgSetupmultiProtocolSearch(CmdOpcode.TAG_READ_MULTIPLE,
                             readPlanList,
                             (TagMetadataFlag)ParamGet("/reader/metadata"),
                             AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_TAG_STREAMING | antsel,
                             timeout);
                    }
                    else
                    {
                        // Coming here means the requested read plan is not for true continuous read.
                        // Throw error back to user.
                        // TODO:Remove this validation, if we need to support for other type of read
                        // operation in future.
                        throw new FeatureNotSupportedException("Unsupported operation");
                    }
                }
                else
                {
                    SimpleReadPlan srp = (SimpleReadPlan)rp;
                    readPlanList.Add(srp);
                    AntennaSelection antsel = PrepForSearch(srp, timeout);
                    cmdMsgMultiple.AddRange(msgSetupmultiProtocolSearch(CmdOpcode.TAG_READ_MULTIPLE,
                                readPlanList,
                                (TagMetadataFlag)ParamGet("/reader/metadata"),
                                antsel,
                                timeout));
                }
                int count = cmdMsgMultiple.Count;
                count = count - 1;
                cmd.Add((byte)count);
                cmd.AddRange(cmdMsgMultiple);
                enableStreaming = false;
            }
            SendM5eCommand(cmd);
            /* If autonomous read is enabled skip RESTORE and CLEAR as 
             * module goes into continuous read mode in this case
             */
            if (!rp.enableAutonomousRead)
            {
                if ((option == UserConfigOperation.RESTORE) || (option == UserConfigOperation.CLEAR))
                {
                    OpenSerialPort(UriPathToCom(_readerUri), ref currentBaudRate); //reprobe the baudrate
                    _universalBaudRate = currentBaudRate;
                    CurrentProtocol = CmdGetProtocol();
                }
            }
        }

        #endregion

        #region msgSetupmultiProtocolSearch
        /// <summary>
        /// lv3 command supporting multiple protocol search
        /// </summary>
        /// <param name="op">opcode</param>
        /// <param name="readPlanList">readplan list</param>
        /// <param name="metadataFlags">metadataflags</param>
        /// <param name="antennas">antenna selection</param>
        /// <param name="timeout">timeout</param>
        /// 
        public List<byte> msgSetupmultiProtocolSearch(CmdOpcode op, List<SimpleReadPlan> readPlanList, TagMetadataFlag metadataFlags, AntennaSelection antennas, ushort timeout)
        {
            List<byte> cmd = new List<byte>();
            int asyncOffTime = (int)ParamGet("/reader/read/asyncOffTime");

            cmd.Add((byte)0x2F);  //Opcode           
            if (enableStreaming)
            {
                cmd.AddRange(ByteConv.EncodeU16(0)); //command timeout
                cmd.Add((byte)(0x01));
            }
            else
            {
                cmd.AddRange(ByteConv.EncodeU16(timeout));
                cmd.Add((byte)(0x11)); //TM Option, turns on metadata
                cmd.AddRange(ByteConv.EncodeU16((ushort)metadataFlags));
            }
            cmd.Add((byte)op);//Cmd opcode

            List<byte> subCmd = new List<byte>(); //protocol specified sub command
            //TagProtocol tagProtocol = TagProtocol.NONE;

            if (readPlanList.Count > 0)
            {
                isSubOffTime = true;
            }

            ushort subTimeout = (ushort)(timeout / readPlanList.Count);
            subOffTimeout = asyncOffTime / readPlanList.Count;

            if (enableStreaming)
            {
                AssignStatusFlags();
            }
            UInt32 totalWeight = 0;
            UInt32 totalTagCount = 0;
            isStopNTags = false;
            foreach (SimpleReadPlan srp in readPlanList)
            {
                totalWeight += Convert.ToUInt32(srp.Weight);
                if (srp is StopTriggerReadPlan)
                {
                    StopTriggerReadPlan strp = (StopTriggerReadPlan)srp;
                    if (strp.stopOnCount is StopOnTagCount)
                    {
                        isStopNTags = true;
                        StopOnTagCount sotc = (StopOnTagCount)strp.stopOnCount;
                        totalTagCount += sotc.N;
                    }
                }
            }
            #region Search flag logic for Dynamic and Read stop trigger
            AntennaSelection searchFlags = 0;
            if (model.Equals("M3e") && isProtocolDynamicSwitching && (readPlanList.Count == 1))
            {
                searchFlags = AntennaSelection.READ_MULTIPLE_SEARCH_DYNAMIC_PROTOCOL_SWITCHING;
            }
            if (isStopNTags)
            {
                searchFlags |= AntennaSelection.READ_MULTIPLE_RETURN_ON_N_TAGS;

            }
            cmd.AddRange(ByteConv.EncodeU16((ushort)searchFlags));
            if (isStopNTags)
            {
                cmd.AddRange(ByteConv.EncodeU32(totalTagCount));
            }
            #endregion
            foreach (SimpleReadPlan readPlan in readPlanList)
            {
                isTriggerReadEnable = false;
                if (readPlan.ReadTrigger != null)
                {
                    if (readPlan.ReadTrigger is GpiPinTrigger)
                    {
                        GpiPinTrigger gpiPinTrigger = (GpiPinTrigger)readPlan.ReadTrigger;
                        isTriggerReadEnable = gpiPinTrigger.enable;
                    }
                }
                subCmd.Clear();

                switch (op)   //assemble subcommand based on type
                {
                    case CmdOpcode.TAG_READ_SINGLE:
                        {
                            subCmd = msgSetupReadTagSingle(metadataFlags, readPlan.Filter, subTimeout);
                            break;
                        }
                    case CmdOpcode.TAG_READ_MULTIPLE:
                        {
                            if (0 != totalWeight)
                            {
                                subTimeout = Convert.ToUInt16(timeout * readPlan.Weight / totalWeight);
                                subOffTimeout = Convert.ToUInt16(asyncOffTime * readPlan.Weight / totalWeight);
                            }
                            //Used temporary variable to avoid appending of the flags, if multiple read plan is used.
                            AntennaSelection tempAntennas = antennas;
                            if (readPlan.UseFastSearch)
                            {
                                antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_FAST_SEARCH;
                            }
                            if (isDutyCycleFlag)
                            {
                                antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_DUTY_CYCLE_CONTROL;
                            }
                            if (isStopNTags )
                            {
                                StopTriggerReadPlan strp = (StopTriggerReadPlan)readPlan;
                                if (strp.stopOnCount is StopOnTagCount)
                                {
                                    StopOnTagCount sotc = (StopOnTagCount)strp.stopOnCount;
                                    numberOfTagsToRead = sotc.N;
                                }
                            }
                            if (null == readPlan.Op)
                            {
                                subCmd = msgSetupReadTagMultiple(subTimeout, antennas, readPlan.Filter, readPlan.Protocol, metadataFlags, 0);
                            }
                            else
                            {
                                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                                int accessPassword = (int)pwobj.Value;
                                msgAddEmbeddedReadOp(ref subCmd, subTimeout, antennas | AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_EMBEDDED_OP, readPlan.Filter, readPlan.Protocol, metadataFlags, accessPassword, readPlan);
                            }
                            antennas = tempAntennas;
                            break;
                        }
                    default:
                        {
                            throw new ReaderException("Operation Not Supported");
                        }
                }

                cmd.Add((byte)TranslateProtocol(readPlan.Protocol));  //protocol ID
                cmd.Add((byte)(subCmd.Count - 1));//PLEN
                cmd.AddRange(subCmd);//protocol sub command
            }
            return cmd;
        }

        #endregion

        #region cmdGetUserProfile

        /// <summary>
        /// Get the configurations from flash.
        /// </summary>
        /// <param name="opCode">the opcode</param>
        /// <returns> The value of the profile parameter</returns>
        public byte[] cmdGetUserProfile(byte[] opCode)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add((byte)0x6D);
            cmd.AddRange(opCode);
            return (GetDataFromM5eResponse(SendM5eCommand(cmd.ToArray())));
        }

        #endregion

        #endregion

        #region Protocol Commands and Utility Methods

        #region CmdGetAvailableProtocols

        /// <summary>
        /// Get the list of RFID protocols supported by the device.
        /// </summary>
        /// <returns>an array of supported protocols</returns>
        public TagProtocol[] CmdGetAvailableProtocols()
        {
            byte[] data = new byte[] { 0x70 };
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));
            TagProtocol[] availableProtocols = new TagProtocol[response.Length / 2];
            int offset = 0;

            for (int i = 0; i < response.Length / 2; i++)
            {
                availableProtocols[i] = TranslateProtocol((SerialTagProtocol)ByteConv.ToU16(response, offset));
                offset += 2;
            }

            return availableProtocols;
        }

        #endregion

        #region CmdSetProtocol

        /// <summary>
        /// Set the current RFID protocol for the device to use.
        /// </summary>
        /// <param name="proto">the protocol to use</param>
        public void CmdSetProtocol(TagProtocol proto)
        {
            CurrentProtocol = proto;
            List<byte> cmd = new List<byte>();
            cmd.Add(0x93);
            cmd.AddRange(ByteConv.EncodeU16((UInt16)TranslateProtocol(proto)));
            SendM5eCommand(cmd);
        }

        #endregion

        #region CmdGetProtocol
        /// <summary>
        /// Get the current RFID protocol the device is configured to use.
        /// </summary>
        /// <returns>the current protocol</returns>
        public TagProtocol CmdGetProtocol()
        {
            byte[] nullArray = new byte[1];
            byte[] data = new byte[1];
            data[0] = 0x63;
            CurrentProtocol = (TranslateProtocol((SerialTagProtocol)(ByteConv.ToU16(GetDataFromM5eResponse(SendM5eCommand(data)), 0))));
            return CurrentProtocol;
        }

        #endregion

        #region CmdGetProtocolConfiguration

        /// <summary>
        /// Gets the value of a protocol configuration setting.
        /// </summary>
        /// <param name="protocol">the protocol of the setting</param>
        /// <param name="key">the setting</param>
        /// <returns>
        /// an object with the setting value. The type of the object
        /// is dependant on the key; see the ProtocolConfiguration class for details.
        /// </returns>
        internal object CmdGetProtocolConfiguration(TagProtocol protocol, ProtocolConfiguration key)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x6B);
            cmd.Add((byte)TranslateProtocol(protocol));
            cmd.Add(key.GetValue());

            switch (protocol)
            {
                case TagProtocol.GEN2:
                    return GetGen2ProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                case TagProtocol.ISO180006B:
                    return GetIso180006bProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                case TagProtocol.ISO14443A:
                    return GetIso14443aProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                case TagProtocol.ISO14443B:
                    return GetIso14443bProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                case TagProtocol.ISO15693:
                    return GetIso15693ProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                case TagProtocol.LF125KHZ:
                    return GetLf125khzProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                case TagProtocol.LF134KHZ:
                    return GetLf134khzProtocolConfigObjectFromByte(key, GetDataFromM5eResponse(SendM5eCommand(cmd)));
                default:
                    throw new ArgumentException("Protocol parameters not supported for protocol " + protocol.ToString());
            }
        }

        #endregion

        #region CmdSetProtocolConfiguration
        /// <summary>
        /// Set Protocol Configuration.
        /// </summary>
        /// <param name="protocol">Protocol</param>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        internal void CmdSetProtocolConfiguration(TagProtocol protocol, ProtocolConfiguration key, object value)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x9B);
            cmd.Add((byte)TranslateProtocol(protocol));
            cmd.Add(key.GetValue());

            if (protocol == TagProtocol.GEN2)
                cmd.AddRange(Gen2ProtocolConfigObjectToByte(key, value));
            else if (protocol == TagProtocol.ISO180006B)
                cmd.AddRange(Iso180006bProtocolConfigObjectToByte(key, value));
            else if (protocol == TagProtocol.ISO14443A)
                cmd.AddRange(Iso14443AProtocolConfigObjectToByte(key, value));
            else if (protocol == TagProtocol.ISO14443B)
                cmd.AddRange(Iso14443BProtocolConfigObjectToByte(key, value));
            else if (protocol == TagProtocol.ISO15693)
                cmd.AddRange(Iso15693ProtocolConfigObjectToByte(key, value));
            else if (protocol == TagProtocol.LF125KHZ)
                cmd.AddRange(Lf125khzProtocolConfigObjectToByte(key, value));
            else if (protocol == TagProtocol.LF134KHZ)
                cmd.AddRange(Lf134khzProtocolConfigObjectToByte(key, value));
            else
                throw new ArgumentException("Protocol parameters not supported for protocol " + protocol.ToString());

            SendM5eCommand(cmd);
        }
        #endregion

        #region CmdSetProtocolList
        /// <summary>
        /// Sets the protocol list as set by the user 
        /// </summary>
        /// <param name="protocolList">the protocol list to be set</param>
        public void CmdSetProtocolList(TagProtocol[] protocolList)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x93); //Opcode for setting tag protocol
            cmd.Add(0x01); // sub option indicating list of protocols
            foreach (TagProtocol proto in protocolList)
            {
                if (!supportedProtocols.Contains(proto))
                {
                    throw new ReaderException("Unsupported protocol " + proto.ToString() + ".");
                }
                cmd.Add((byte)0x00);
                cmd.Add((byte)TranslateProtocol(proto));
            }
            SendM5eCommand(cmd);

            //If user sets this param - "/reader/protocolList", it indicates API has to do dynamic Switching of protocols.
            // Enable the isProtocolDynamicSwitching flag to do so.
            isProtocolDynamicSwitching = true;
        }
        #endregion

        #region CmdGetProtocolList
        /// <summary>
        /// Gets the protocol list set on the module 
        /// </summary>
        public TagProtocol[] CmdGetProtocolList()
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x63); //Opcode for getting tag protocol
            cmd.Add(0x01); // sub option indicating list of protocols
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(cmd));
            TagProtocol[] protocolsList = new TagProtocol[response.Length / 2];
            int offset = 1; //skip option byte
            for (int i = 0; i < response.Length / 2; i++)
            {
                protocolsList[i] = TranslateProtocol((SerialTagProtocol)ByteConv.ToU16(response, offset));
                offset += 2;
            }
            return protocolsList;
        }
        #endregion

        #region TranslateProtocol

        /// <summary>
        /// Translate tag protocol codes from M5e internal to ThingMagic external representation.
        /// </summary>
        /// <param name="intProt">Internal M5e tag protocol code</param>
        /// <returns>External ThingMagic protocol code</returns>
        private static ThingMagic.TagProtocol TranslateProtocol(SerialTagProtocol intProt)
        {
            switch (intProt)
            {
                case SerialTagProtocol.NONE:
                    return ThingMagic.TagProtocol.NONE;
                case SerialTagProtocol.GEN2:
                    return ThingMagic.TagProtocol.GEN2;
                case SerialTagProtocol.ISO18000_6B:
                    return ThingMagic.TagProtocol.ISO180006B;
                case SerialTagProtocol.UCODE:
                    return ThingMagic.TagProtocol.ISO180006B_UCODE;
                case SerialTagProtocol.IPX64:
                    return ThingMagic.TagProtocol.IPX64;
                case SerialTagProtocol.IPX256:
                    return ThingMagic.TagProtocol.IPX256;
                case SerialTagProtocol.ATA:
                    return ThingMagic.TagProtocol.ATA;
                case SerialTagProtocol.ISO14443A:
                    return ThingMagic.TagProtocol.ISO14443A;
                case SerialTagProtocol.ISO14443B:
                    return ThingMagic.TagProtocol.ISO14443B;
                case SerialTagProtocol.ISO15693:
                    return ThingMagic.TagProtocol.ISO15693;
                case SerialTagProtocol.ISO18092:
                    return ThingMagic.TagProtocol.ISO18092;
                case SerialTagProtocol.FELICA:
                    return ThingMagic.TagProtocol.FELICA;
                case SerialTagProtocol.ISO180003M3:
                    return ThingMagic.TagProtocol.ISO180003M3;
                case SerialTagProtocol.LF125KHZ:
                    return ThingMagic.TagProtocol.LF125KHZ;
                case SerialTagProtocol.LF134KHZ:
                    return ThingMagic.TagProtocol.LF134KHZ;
                default:
                    return ThingMagic.TagProtocol.NONE;
                //throw new ReaderParseException("No translation for M5e tag protocol code: " + intProt.ToString());
            }
        }

        /// <summary>
        /// Translate tag protocol codes from M5e internal to ThingMagic external representation.
        /// </summary>
        /// <param name="intProt">Internal M5e tag protocol code</param>
        /// <returns>External ThingMagic protocol code</returns>
        private static SerialTagProtocol TranslateProtocol(ThingMagic.TagProtocol intProt)
        {
            switch (intProt)
            {
                case ThingMagic.TagProtocol.NONE:
                    return SerialTagProtocol.NONE;
                case ThingMagic.TagProtocol.GEN2:
                    return SerialTagProtocol.GEN2;
                case ThingMagic.TagProtocol.ISO180006B:
                    return SerialTagProtocol.ISO18000_6B;
                case ThingMagic.TagProtocol.ISO180006B_UCODE:
                    return SerialTagProtocol.UCODE;
                case ThingMagic.TagProtocol.IPX64:
                    return SerialTagProtocol.IPX64;
                case ThingMagic.TagProtocol.IPX256:
                    return SerialTagProtocol.IPX256;
                case ThingMagic.TagProtocol.ATA:
                    return SerialTagProtocol.ATA;
                case ThingMagic.TagProtocol.ISO14443A:
                    return SerialTagProtocol.ISO14443A;
                case ThingMagic.TagProtocol.ISO14443B:
                    return SerialTagProtocol.ISO14443B;
                case ThingMagic.TagProtocol.ISO15693:
                    return SerialTagProtocol.ISO15693;
                case ThingMagic.TagProtocol.ISO18092:
                    return SerialTagProtocol.ISO18092;
                case ThingMagic.TagProtocol.FELICA:
                    return SerialTagProtocol.FELICA;
                case ThingMagic.TagProtocol.ISO180003M3:
                    return SerialTagProtocol.ISO180003M3;
                case ThingMagic.TagProtocol.LF125KHZ:
                    return SerialTagProtocol.LF125KHZ;
                case ThingMagic.TagProtocol.LF134KHZ:
                    return SerialTagProtocol.LF134KHZ;
                default:
                    return SerialTagProtocol.NONE;
                //throw new ReaderParseException("No translation for ThingMagic tag protocol code: " + intProt.ToString());
            }
        }

        #endregion

        #region GetGen2ProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetGen2ProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x00:
                    {
                        switch (value[2])
                        {
                            case 0x00:
                                return Gen2.Session.S0;
                            case 0x01:
                                return Gen2.Session.S1;
                            case 0x02:
                                return Gen2.Session.S2;
                            case 0x03:
                                return Gen2.Session.S3;
                            default:
                                throw new ArgumentException("Unknown Session type " + value[2].ToString());
                        }
                    }
                case 0x01:
                    {
                        if ((value[2] == 0x01) && (value[3] == 0x00))
                        {
                            return Gen2.Target.A;
                        }
                        else if ((value[2] == 0x01) && (value[3] == 0x01))
                        {
                            return Gen2.Target.B;
                        }
                        else if ((value[2] == 0x00) && (value[3] == 0x00))
                        {
                            return Gen2.Target.AB;
                        }
                        else if ((value[2] == 0x00) && (value[3] == 0x01))
                        {
                            return Gen2.Target.BA;
                        }
                        else
                        {
                            throw new ArgumentException("Unknown Target option " + value[2].ToString() + "and value " + value[3].ToString());
                        }
                    }
                case 0x02:
                    {
                        switch (value[2])
                        {
                            case 0x00:
                                return Gen2.TagEncoding.FM0;
                            case 0x01:
                                return Gen2.TagEncoding.M2;
                            case 0x02:
                                return Gen2.TagEncoding.M4;
                            case 0x03:
                                return Gen2.TagEncoding.M8;

                            default:
                                throw new ArgumentException("Unknown Miller type " + value[2].ToString());
                        }
                    }
                case 0x10:
                    {
                        switch (value[2])
                        {
                            case 0x00:
                                return serialGen2LinkFrequency.LINK250KHZ;
                            case 0x02:
                                return serialGen2LinkFrequency.LINK320KHZ;
                            case 0x04:
                                return serialGen2LinkFrequency.LINK640KHZ;
                            case 0x06:
                                return serialGen2LinkFrequency.LINK640KHZ;
                            default:
                                throw new ArgumentException("Unknown Link Frequency value " + value[2].ToString());
                        }
                    }
                case 0x11:
                    {
                        switch (value[2])
                        {
                            case 0x00:
                                return Gen2.Tari.TARI_25US;
                            case 0x01:
                                return Gen2.Tari.TARI_12_5US;
                            case 0x02:
                                return Gen2.Tari.TARI_6_25US;
                            default:
                                throw new ArgumentException("Unknown Tari value " + value[2].ToString());
                        }
                    }
                case 0x12:
                    {
                        switch (value[2])
                        {
                            case 0x00:
                                return new Gen2.DynamicQ();
                            case 0x01:
                                return new Gen2.StaticQ(value[3]);
                            default:
                                throw new ArgumentException("Unknown Q Algorithm " + value[2].ToString());
                        }
                    }
                case 0x13:
                    {
                        int offset = 5;
                        Gen2.BAPParameters bapParams = new Gen2.BAPParameters();
                        bapParams.POWERUPDELAY = (Int32)ByteConv.ToU32(value, offset);
                        offset += 4;
                        bapParams.FREQUENCYHOPOFFTIME = (Int32)ByteConv.ToU32(value, offset);
                        return bapParams;
                    }
                case 0x14:
                    {
                        switch (value[2])
                        {
                            case 0x00:
                                return Gen2.ProtocolExtension.LICENSE_NONE;
                            case 0x01:
                                return Gen2.ProtocolExtension.LICENSE_IAV_DENATRAN;
                            default:
                                throw new ArgumentException("Unknown Protocol Extension value " + value[2].ToString());
                        }
                    }
                case 0x15:
                    {
                        int offset = 2;
                        UInt32 t4Value = ByteConv.ToU32(value, offset);
                        return t4Value;
                    }
                case 0x16:
                    {
                        Gen2.InitQ q = new Gen2.InitQ();
                        if (value[2] == 1)
                        {
                            q.qEnable = true;
                        }
                        else if (value[2] == 0)
                        {
                            q.qEnable = false;
                        }
                        q.initialQ = value[3];
                        return q;
                    }
                case 0x17:
                    int sendSelectValue = value[2];
                    if (sendSelectValue == 1)
                        return true;
                    else
                        return false;

                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }
        #endregion

        #region Gen2ProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Gen2ProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();

            switch (key.GetValue())
            {
                case 0x00:
                    {
                        switch ((Gen2.Session)value)
                        {
                            case Gen2.Session.S0:
                                {
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Session.S1:
                                {
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Session.S2:
                                {
                                    returnArray.Add(0x02);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Session.S3:
                                {
                                    returnArray.Add(0x03);
                                    return returnArray.ToArray();
                                }
                            default:
                                throw new ArgumentException("Unknown Session Value " + value.ToString());
                        }
                    }
                case 0x01:
                    {
                        switch ((Gen2.Target)value)
                        {
                            case Gen2.Target.A:
                                {
                                    returnArray.Add(0x01);
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Target.B:
                                {
                                    returnArray.Add(0x01);
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Target.AB:
                                {
                                    returnArray.Add(0x00);
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Target.BA:
                                {
                                    returnArray.Add(0x00);
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            default:
                                throw new ArgumentException("Unknown Target Value " + value.ToString());
                        }
                    }
                case 0x02:
                    {
                        switch ((Gen2.TagEncoding)value)
                        {
                            case Gen2.TagEncoding.FM0:
                                {
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case Gen2.TagEncoding.M2:
                                {
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            case Gen2.TagEncoding.M4:
                                {
                                    returnArray.Add(0x02);
                                    return returnArray.ToArray();
                                }
                            case Gen2.TagEncoding.M8:
                                {
                                    returnArray.Add(0x03);
                                    return returnArray.ToArray();
                                }
                            default:
                                throw new ArgumentException("Unknown TagEncoding Value " + value.ToString());
                        }
                    }
                case 0x10:
                    {
                        switch ((serialGen2LinkFrequency)value)
                        {
                            case serialGen2LinkFrequency.LINK250KHZ:
                                {
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case serialGen2LinkFrequency.LINK320KHZ:
                                {
                                    returnArray.Add(0x02);
                                    return returnArray.ToArray();
                                }
                            case serialGen2LinkFrequency.LINK640KHZ:
                                {
                                    returnArray.Add(0x04);
                                    return returnArray.ToArray();
                                }
                            default: throw new ArgumentException("Unsupported Link Frequency Value " + value.ToString());
                        }
                    }
                case 0x11:
                    {
                        switch ((Gen2.Tari)value)
                        {
                            case Gen2.Tari.TARI_25US:
                                {
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Tari.TARI_12_5US:
                                {
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            case Gen2.Tari.TARI_6_25US:
                                {
                                    returnArray.Add(0x02);
                                    return returnArray.ToArray();
                                }

                            default: throw new ArgumentException("Unknown Tari Value " + value.ToString());
                        }
                    }
                case 0x12:
                    {
                        if (value is Gen2.DynamicQ)
                        {
                            returnArray.Add(0x00);
                            return returnArray.ToArray();
                        }

                        else if (value is Gen2.StaticQ)
                        {
                            returnArray.Add(0x01);
                            returnArray.Add(((Gen2.StaticQ)value).InitialQ);
                            return returnArray.ToArray();
                        }
                        else throw new ArgumentException("Unknown Q Algorithm " + value.ToString());
                    }
                case 0x13:
                    {
                        // Version
                        returnArray.Add(0x01);
                        // A bit mask detailing what parts of BAP support are enabled.
                        returnArray.Add(0x00);
                        returnArray.Add(0x03);
                        // Power up delay
                        returnArray.AddRange(ByteConv.EncodeU32(Convert.ToUInt32(((Gen2.BAPParameters)value).POWERUPDELAY)));
                        // Frequency hop off time
                        returnArray.AddRange(ByteConv.EncodeU32(Convert.ToUInt32(((Gen2.BAPParameters)value).FREQUENCYHOPOFFTIME)));
                        // Currently support for M value and FlexQueryPayload has been disabled
                        // TODO: add support for these parameters when firmware support has added for this.
                        return returnArray.ToArray();
                    }
                case 0x14:
                    {
                        switch ((Gen2.ProtocolExtension)value)
                        {
                            case Gen2.ProtocolExtension.LICENSE_NONE:
                                {
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            case Gen2.ProtocolExtension.LICENSE_IAV_DENATRAN:
                                {
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            default:
                                throw new ArgumentException("Unknown Protocol Extension Value " + value.ToString());
                        }
                    }
                case 0x15:
                    {
                        returnArray.AddRange(ByteConv.EncodeU32(Convert.ToUInt32(value)));
                        return returnArray.ToArray();
                    }
                case 0x16:
                    {
                        Gen2.InitQ qQ = (Gen2.InitQ)value;
                        bool flag = qQ.qEnable;
                        if (flag)
                        {
                            returnArray.Add(0x01);
                            returnArray.Add((byte)qQ.initialQ);
                        }
                        else
                        {
                            returnArray.Add(0x00);
                        }
                        return returnArray.ToArray();
                    }
                case 0x17:
                    {
                        bool status = (Boolean)value;
                        if (status)
                        {
                            returnArray.Add(0x01);
                        }
                        else
                        {
                            returnArray.Add(0x00);
                        }
                        return returnArray.ToArray();
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }
        #endregion

        #region ISO180006BProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Iso180006bProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();

            switch (key.GetValue())
            {
                case 0x10:
                    {
                        switch ((serialIso180006bLinkFrequency)value)
                        {
                            case serialIso180006bLinkFrequency.LINK40KHZ:
                                {
                                    returnArray.Add(0x01);
                                    return returnArray.ToArray();
                                }
                            case serialIso180006bLinkFrequency.LINK160KHZ:
                                {
                                    returnArray.Add(0x00);
                                    return returnArray.ToArray();
                                }
                            default:
                                throw new ArgumentException("Unsupported Link Frequency Value " + value.ToString());
                        }
                    }
                case 0x11:
                    {
                        if (!Enum.IsDefined(typeof(Iso180006b.ModulationDepth), (int)value))
                        {
                            throw new ArgumentException("Unsupported Modulation Depth Value " + value.ToString());
                        }
                        else
                        {
                            returnArray.Add(Convert.ToByte(value));
                            return returnArray.ToArray();
                        }
                    }
                case 0x12:
                    {
                        if (!Enum.IsDefined(typeof(Iso180006b.Delimiter), (int)value))
                        {
                            throw new ArgumentException("Unsupported Delimiter Value " + value.ToString());
                        }
                        else
                        {
                            returnArray.Add(Convert.ToByte(value));
                            return returnArray.ToArray();
                        }
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }

        #endregion

        #region GetIso180006bProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetIso180006bProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x10:
                    {
                        switch (value[2])
                        {
                            case 0x01:
                                return serialIso180006bLinkFrequency.LINK40KHZ;
                            case 0x00:
                                return serialIso180006bLinkFrequency.LINK160KHZ;
                            default:
                                throw new ArgumentException("Unknown Link Frequency type " + value[2].ToString());
                        }
                    }
                case 0x11:
                    {
                        if (!Enum.IsDefined(typeof(Iso180006b.ModulationDepth), (int)value[2]))
                        {
                            throw new ArgumentException("Unknown Modulation Depth type " + value[2].ToString());
                        }
                        else
                        {
                            return (Iso180006b.ModulationDepth)value[2];
                        }
                    }
                case 0x12:
                    {
                        if (!Enum.IsDefined(typeof(Iso180006b.Delimiter), (int)value[2]))
                        {
                            throw new ArgumentException("Unknown Delimiter type " + value[2].ToString());
                        }
                        else
                        {
                            return (Iso180006b.Delimiter)value[2];
                        }
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + key.ToString());
            }
        }
        #endregion

        #region Iso14443AProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array. 
        /// To extend the flag byte, an EBV technique is to be used. When the highest order bit of the flag
        /// is used, it signals the reader's parser, that another flag byte is to follow.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Iso14443AProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();
            switch (key.GetValue())
            {
                case 0x02:
                    {
                        returnArray = ConvertToEBV(value);
                        return returnArray.ToArray();
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }

        #endregion

        #region Iso14443BProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array. 
        /// To extend the flag byte, an EBV technique is to be used. When the highest order bit of the flag
        /// is used, it signals the reader's parser, that another flag byte is to follow.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Iso14443BProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();
            switch (key.GetValue())
            {
                case 0x02:
                    {
                        returnArray = ConvertToEBV(value);
                        return returnArray.ToArray();
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }

        #endregion

        #region GetIso14443aProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetIso14443aProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x01:
                case 0x02:
                case 0x03:
                    {
                        byte[] ebvTagTypeVal = new byte[value.Length - 2];
                        Array.Copy(value, 2, ebvTagTypeVal, 0, (value.Length - 2));
                        return (Iso14443a.TagType)ConvertFromEBV(ebvTagTypeVal);
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + key.ToString());
            }
        }
        #endregion

        #region GetIso14443bProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetIso14443bProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x01:
                case 0x02:
                    {
                        byte[] ebvTagTypeVal = new byte[value.Length - 2];
                        Array.Copy(value, 2, ebvTagTypeVal, 0, (value.Length - 2));
                        return (Iso14443b.TagType)ConvertFromEBV(ebvTagTypeVal);
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + key.ToString());
            }
        }
        #endregion

        #region Iso15693ProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Iso15693ProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();
            switch (key.GetValue())
            {
                case 0x02:
                    {
                        returnArray = ConvertToEBV(value);
                        return returnArray.ToArray();
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }

        #endregion

        #region Lf125khzProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Lf125khzProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();
            switch (key.GetValue())
            {
                case 0x02:
                    {
                        returnArray = ConvertToEBV(value);
                        return returnArray.ToArray();
                    }
                case 0x04:
                    {
                        returnArray.Add((byte)((Lf125khz.NHX_Type)value));
                        return returnArray.ToArray();
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }

        #endregion

        #region Lf134khzProtocolConfigObjectToByte

        /// <summary>
        /// Private function to convert Protocol Configuration Object to Byte array.
        /// </summary>
        /// <param name="key">Protocol Configuration Key</param>
        /// <param name="value">Protocol Configuration Value</param>
        /// <returns></returns>
        private static byte[] Lf134khzProtocolConfigObjectToByte(ProtocolConfiguration key, object value)
        {
            List<byte> returnArray = new List<byte>();
            switch (key.GetValue())
            {
                case 0x02:
                    {
                        returnArray = ConvertToEBV(value);
                        return returnArray.ToArray();
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + value.ToString());
            }
        }

        #endregion

        #region GetIso15693ProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetIso15693ProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x01:
                case 0x02:
                case 0x03:
                    {
                        byte[] ebvTagTypeVal = new byte[value.Length - 2];
                        Array.Copy(value, 2, ebvTagTypeVal, 0, (value.Length - 2));
                        return (Iso15693.TagType)ConvertFromEBV(ebvTagTypeVal);
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + key.ToString());
            }
        }
        #endregion

        #region GetLf125khzProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetLf125khzProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x01:
                case 0x02:
                case 0x03:
                    {
                        byte[] ebvTagTypeVal = new byte[value.Length - 2];
                        Array.Copy(value, 2, ebvTagTypeVal, 0, (value.Length - 2));
                        return (Lf125khz.TagType)ConvertFromEBV(ebvTagTypeVal);
                    }
                case 0x04:
                    {
                        return (Lf125khz.NHX_Type)value[value.Length - 1];
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + key.ToString());
            }
        }
        #endregion

        #region GetLf134khzProtocolConfigObjectFromByte

        /// <summary>
        /// private function to convert bytes to protocol configuration based objects
        /// </summary>
        /// <param name="key">Protocol configuration key</param>
        /// <param name="value">Value in bytes</param>
        /// <returns>Protocol configuration object</returns>
        private static object GetLf134khzProtocolConfigObjectFromByte(ProtocolConfiguration key, byte[] value)
        {
            switch (key.GetValue())
            {
                case 0x01:
                case 0x02:
                    {
                        byte[] ebvTagTypeVal = new byte[value.Length - 2];
                        Array.Copy(value, 2, ebvTagTypeVal, 0, (value.Length - 2));
                        return (Lf134khz.TagType)ConvertFromEBV(ebvTagTypeVal);
                    }
                default:
                    throw new ArgumentException("Unknown Protocol Parameter " + key.ToString());
            }
        }
        #endregion

        #region ConvertToEBV

        /// <summary>
        /// Function to convert actual data to EBV Format
        /// </summary>
        /// <param name="value">user set value </param>
        /// <returns>List of bytes containing data in EBV format</returns>
        public static List<byte> ConvertToEBV(Object value)
        {

            List<byte> returnArray = new List<byte>();

            /* Allocation of number of bytes for a data which has to be represented in EBV format is dynamic. 
             * It purely based on the EBV format and user set value.
             * Convert the user set value to EBV format value as per below logic.
             */
            if ((0x80) > Convert.ToUInt32(value)) // 1 byte is sufficient for representation
            {
                returnArray.Add(Convert.ToByte(value));
            }
            else if ((0x4000) > Convert.ToUInt32(value))  // 2 bytes are sufficient for representation
            {
                ushort ushortFlag = Convert.ToUInt16(value);
                ushort temp = (ushort)(ushortFlag & 0x7f);
                ushortFlag &= 0xFF80;
                ushortFlag = (ushort)((ushortFlag << 1) | temp);
                returnArray.AddRange(ByteConv.EncodeU16(Convert.ToUInt16(0x8000 | ushortFlag)));
            }
            else if ((0x200000) > Convert.ToUInt32(value)) // 3 bytes are sufficient for representation flag but no such primitive data type to store 3 bytes. Hence using 4 bytes.
            {
                UInt32 uintFlag = Convert.ToUInt32(value);
                UInt32 temp = (UInt32)(uintFlag & 0x7f);
                uintFlag = (UInt32)(uintFlag << 1);
                temp |= (uintFlag & 0x7f00);
                uintFlag = (UInt32)((uintFlag << 1) & 0xFF0000) | temp;
                returnArray.AddRange(ByteConv.EncodeU24(Convert.ToUInt32(0x808000 | uintFlag)));
            }
            else if ((0x10000000) > Convert.ToUInt32(value)) // 4 bytes are sufficient for representation
            {
                UInt32 uFlag = Convert.ToUInt32(value);
                UInt32 temp = (UInt32)(uFlag & 0x7f);
                uFlag = (UInt32)(uFlag << 1);
                temp |= (uFlag & 0x7f00);
                uFlag = (UInt32)(uFlag << 1);
                temp |= (uFlag & 0x7f0000);
                uFlag = (UInt32)((uFlag << 1) & 0xFF000000) | temp;
                returnArray.AddRange(ByteConv.EncodeU32(Convert.ToUInt32(0x80808000 | uFlag)));
            }
            else if ((0x800000000) > Convert.ToUInt64(value))
            {
                UInt64 uFlgVal = Convert.ToUInt64(value); //5 bytes are sufficient for representation, but no such primitive data type to store 5 bytes. Hence using UInt64.
                UInt64 temp = (uFlgVal & 0x7f);
                uFlgVal = (UInt64)(uFlgVal << 1);
                temp |= (uFlgVal & 0x7f00);
                uFlgVal = (UInt64)(uFlgVal << 1);
                temp |= (uFlgVal & 0x7f0000);
                uFlgVal = (UInt64)(uFlgVal << 1);
                temp |= (uFlgVal & 0x7f000000);
                uFlgVal = (UInt64)(((uFlgVal << 1) & 0xff00000000) | temp);
                returnArray.AddRange(ByteConv.EncodeU40(Convert.ToUInt64(0x8080808000 | uFlgVal)));
            }
            else
            {
                throw new ArgumentException("Unknown Value " + value.ToString());
            }
            return returnArray;
        }
        #endregion

        #region ConvertFromEBV

        /// <summary>
        /// Function to convert EBV Format value to actual value
        /// </summary>
        /// <param name="value">Value in bytes</param>
        /// <returns>the data in 64 bit</returns>
        public static UInt64 ConvertFromEBV(byte[] value)
        {
            {
                // Parse response based on tag type length.
                switch (value.Length)
                {
                    // convert the EBV format data back to user set value.
                    case 0x01:
                        return (uint)ByteConv.GetU8(value, 0);
                    case 0x02:
                        ushort flg = ByteConv.ToU16(value, 0);
                        ushort temp = (ushort)(flg & 0x7f);
                        flg &= 0x7fff;
                        flg = (ushort)(((flg >> 1) & 0xff80) | temp);
                        return flg;
                    case 0x03:
                        UInt32 type = (uint)ByteConv.GetU24(value, 0);
                        type &= 0x7f7f7f;
                        UInt32 tmp = type & 0x7f;
                        type = ((type >> 1) | tmp);
                        tmp |= type & 0x3f80;
                        type = (((type >> 1) & 0xffc000) | tmp);
                        return type;
                    case 0x04:
                        UInt32 flag = ByteConv.ToU32(value, 0);
                        flag &= 0x7f7f7f7f;
                        UInt32 tempVar = flag & 0x7f;
                        flag = (flag >> 1 | tempVar);
                        tempVar |= flag & 0x3f80;
                        flag = ((flag >> 1) & 0xffffc000 | tempVar);
                        tempVar |= flag & 0x1fc000;
                        flag = (((flag >> 1) & 0xffe00000) | tempVar);
                        return flag;
                    case 0x05:
                        UInt64 flgVal = ByteConv.ToU40(value, 0);
                        flgVal &= 0x7f7f7f7f7f;
                        UInt64 tempVal = (flgVal & 0x7f);
                        flgVal = ((flgVal >> 1) | tempVal);
                        tempVal |= (flgVal & 0x3f80);
                        flgVal = (((flgVal >> 1) & 0xffffffc000) | tempVal);
                        tempVal |= (flgVal & 0x1fc000);
                        flgVal = (((flgVal >> 1) & 0xffffe00000) | tempVal);
                        tempVal |= (flgVal & 0x1fe00000);
                        flgVal = (((flgVal >> 1) & 0xfff0000000) | tempVal);
                        return flgVal;
                    default:
                        throw new ArgumentException("Unknown value detected");
                }
            }
        }
        #endregion

        #endregion

        #region Frequency HOP Table Commands

        #region FrequencyHopTableOption

        /// <summary>
        /// Frequency hoptable options
        /// </summary>
        public enum FrequencyHopTableOption
        {
            /// <summary>
            /// Option to specify hoptime
            /// </summary>
            HOPTIME = 0x01,
            /// <summary>
            /// Option to specify quantization step
            /// </summary>
            QUANTIZATION_STEP = 0x02,
            /// <summary>
            /// Option to specify minimum frequency
            /// </summary>
            MINIMUM_FREQUENCY = 0x03,
        }

        # endregion

        #region CmdGetFrequencyHopTable

        /// <summary>
        /// Gets the frequencies in the current hop table
        /// </summary>
        /// <returns>an array of the frequencies in the hop table, in kHz</returns>
        public UInt32[] CmdGetFrequencyHopTable()
        {
            byte[] data = new byte[1];
            data[0] = 0x65;
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));
            UInt32[] frequencies = new UInt32[response.Length / 4];
            int offset = 0;
            for (int i = 0; i < response.Length / 4; i++)
            {
                frequencies[i] = ByteConv.ToU32(response, offset);
                offset += 4;
            }
            return frequencies;
        }

        #endregion

        #region CmdGetFrequencyHopTime

        /// <summary>
        /// Gets the interval between frequency hops.
        /// </summary>
        /// <returns>the hop interval, in milliseconds</returns>
        public UInt32 CmdGetFrequencyHopTime()
        {
            byte[] data = new byte[2];
            data[0] = 0x65;
            data[1] = (byte)FrequencyHopTableOption.HOPTIME;
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));
            return ByteConv.ToU32(response, 1);
        }

        #endregion

        #region CmdGetQuantizationStep

        /// <summary>
        /// Gets the interval between frequency hops.
        /// </summary>
        /// <returns>the hop interval, in milliseconds</returns>
        public UInt32 CmdGetQuantizationStep()
        {
            byte[] data = new byte[2];
            data[0] = 0x65;
            data[1] = (byte)FrequencyHopTableOption.QUANTIZATION_STEP;
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));
            return ByteConv.ToU32(response, 1);
        }

        #endregion

        #region CmdGetMinimumFrequency

        /// <summary>
        /// Gets the interval between frequency hops.
        /// </summary>
        /// <returns>the hop interval, in milliseconds</returns>
        public UInt32 CmdGetMinimumFrequency()
        {
            byte[] data = new byte[2];
            data[0] = 0x65;
            data[1] = (byte)FrequencyHopTableOption.MINIMUM_FREQUENCY;
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));
            return ByteConv.ToU32(response, 1);
        }

        #endregion

        #region CmdSetFrequencyHopTable

        /// <summary>
        /// Set the frequency hop table.
        /// </summary>
        /// <param name="frequency">A list of frequencies, in kHz. The list may be at most 62 elements.</param>
        public void CmdSetFrequencyHopTable(UInt32[] frequency)
        {
            List<byte> data = new List<byte>();
            data.Add(0x95);
            for (int i = 0; i < frequency.Length; i++)
                data.AddRange(ByteConv.EncodeU32(frequency[i]));
            SendM5eCommand(data);
        }

        #endregion

        #region CmdSetFrequencyHopTime

        /// <summary>
        /// Set the interval between frequency hops. The valid range for this
        /// interval is region-dependent.
        /// </summary>
        /// <param name="hopTime">the hop interval, in milliseconds</param>
        public void CmdSetFrequencyHopTime(UInt32 hopTime)
        {
            List<byte> data = new List<byte>();
            data.Add(0x95);
            data.Add((byte)FrequencyHopTableOption.HOPTIME);
            data.AddRange(ByteConv.EncodeU32(hopTime));
            SendM5eCommand(data);
        }

        #endregion

        #region CmdSetQuantizationStep

        /// <summary>
        /// Set Quantization step value.
        /// </summary>
        /// <param name="step">Quantization step value to be set, in kHz</param>
        public void CmdSetQuantizationStep(UInt32 step)
        {
            List<byte> data = new List<byte>();
            data.Add(0x95);
            data.Add((byte)FrequencyHopTableOption.QUANTIZATION_STEP);
            data.AddRange(ByteConv.EncodeU32(step));
            SendM5eCommand(data);
        }

        #endregion

        #region CmdSetMinimumFrequency

        /// <summary>
        /// Set Minimum frequency value.
        /// </summary>
        /// <param name="freq">Minimum frequency value to be set, in kHz</param>
        public void CmdSetMinimumFrequency(UInt32 freq)
        {
            List<byte> data = new List<byte>();
            data.Add(0x95);
            data.Add((byte)FrequencyHopTableOption.MINIMUM_FREQUENCY);
            data.AddRange(ByteConv.EncodeU32(freq));
            SendM5eCommand(data);
        }

        #endregion

        #endregion

        #region Search and Tag Commands and Utility Methods

        #region setProtocol

        private void setProtocol(TagProtocol proto)
        {
            if (CurrentProtocol != proto)
            {
                CmdSetProtocol(proto);
                if (isExtendedEpc)
                {
                    CmdSetReaderConfiguration(Configuration.EXTENDED_EPC, true);
                }
                CmdSetReaderConfiguration(Configuration.ENABLE_FILTERING, _enableFiltering);
                if (!(model.Equals("M3e")))
                {
                    int moduleValue = (DEFAULT_READ_FILTER_TIMEOUT == _readFilterTimeout) ? 0 : _readFilterTimeout;
                    CmdSetReaderConfiguration(Configuration.TAG_BUFFER_ENTRY_TIMEOUT, moduleValue);
                }
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// Read RFID tags for a fixed duration.
        /// </summary>
        /// <param name="timeout">the time to spend reading tags, in milliseconds</param>
        /// <returns>the tags read</returns>
        public override TagReadData[] Read(int timeout)
        {
            //CheckRegion();
            if (!_runNow)
            {
                tagOpSuccessCount = 0;
                tagOpFailuresCount = 0;
            }
            if (timeout < 0)
                throw new ArgumentOutOfRangeException("Timeout (" + timeout.ToString() + ") must be greater than or equal to 0");

            else if (timeout > 65535)
                throw new ArgumentOutOfRangeException("Timeout (" + timeout.ToString() + ") must be less than 65536");

            List<TagReadData> tagReads = new List<TagReadData>();

            // Send clear tag buffer only for sync read
            if (!isTrueContinuousRead)
            {
                CmdClearTagBuffer();
            }
            // If reader stats is supported by the reader, then reset the stats
            if (isSupportsResetStats&&!(isResetReaderStatsSent))
            {
                try
                {
                    CmdResetReaderStats(new Reader.Stat());
                    isResetReaderStatsSent = true;
                }
                catch (Exception ex)
                {
                    if ((ex is FAULT_MSG_WRONG_NUMBER_OF_DATA_Exception)
                        || (ex is FAULT_INVALID_OPCODE_Exception)
                        || (ex is FAULT_UNIMPLEMENTED_OPCODE_Exception)
                        || (ex is FAULT_UNIMPLEMENTED_FEATURE_Exception))
                    {
                        // If command unsupported, make a note not to do it again then
                        // proceed normally 
                        isSupportsResetStats = false;
                    }
                }
            }
            ReadInternal((UInt16)timeout, (ReadPlan)ParamGet("/reader/read/plan"), ref tagReads);

            return tagReads.ToArray();
        }

        #endregion

        #region ReadInternal
        private void ReadInternal(UInt16 timeout, ReadPlan rp, ref List<TagReadData> tagReads)
        {
            // Reset number of tags to zero
            numberOfTagsToRead = 0;
            isStopNTags = false;
            if (rp is MultiReadPlan)
            {
                MultiReadPlan mrp = (MultiReadPlan)rp;
                isValidationSuccess = validateMultiReadPlan(mrp);
                List<SimpleReadPlan> readPlanList = new List<SimpleReadPlan>();
                foreach (ReadPlan r in mrp.Plans)
                {
                    SimpleReadPlan srp = (SimpleReadPlan)r;
                    readPlanList.Add(srp);
                }
                //dynamic protocol switching is supported only for async read with simple readplan
                if ((model.Equals("M3e")) && isProtocolDynamicSwitching)
                {
                    isProtocolDynamicSwitching = false;
                    throw new Exception("Unsupported operation.");
                }

                if ((((MODEL_M6E == _version.Hardware.Part1) ||
                    (MODEL_M6E_I == _version.Hardware.Part1) ||
                    (MODEL_MICRO == _version.Hardware.Part1) ||
                    (MODEL_M6E_NANO == _version.Hardware.Part1) ||
                    (MODEL_M3E == _version.Hardware.Part1)) ||
                    enableStreaming) && (isValidationSuccess))
                {
                    // True continuous read, if there's a consistent antenna list
                    // across the entire set of read plans.

                    AntennaSelection antsel = PrepForSearch(rp, timeout);

                    //Disable ReadFilter in case of continuous Read 
                    //and enable in case of sync read.
                    bool enableFilteringToSet = enableStreaming ? false : true;
                    if ((enableFilteringToSet != _enableFiltering) && (!userEnableReadFiltering))
                    {
                        CmdSetReaderConfiguration(Configuration.ENABLE_FILTERING, enableFilteringToSet);
                        _enableFiltering = enableFilteringToSet;
                    }

                    if (enableStreaming) //using streaming
                    {
                        //ReadFilter need to be disabled for continuous Read
                        tagReads.AddRange(CmdMultiProtocolSearch(
                            CmdOpcode.TAG_READ_MULTIPLE,
                            readPlanList,
                            (TagMetadataFlag)ParamGet("/reader/metadata"),
                            AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_TAG_STREAMING | antsel,
                            timeout));
                    }
                    else //no streaming
                    {
                        DateTime now = DateTime.Now;
                        DateTime endTime = now.AddMilliseconds(timeout);

                        while (now <= endTime)
                        {
                            TimeSpan timeElapsed = endTime - now;
                            timeout = ((ushort)timeElapsed.TotalMilliseconds < 65535) ? (ushort)timeElapsed.TotalMilliseconds : (ushort)65535;

                            DateTime searchStart = DateTime.Now;

                            try
                            {
                                tagReads.AddRange(CmdMultiProtocolSearch(
                            CmdOpcode.TAG_READ_MULTIPLE,
                            readPlanList,
                            (TagMetadataFlag)ParamGet("/reader/metadata"),
                             0 | antsel,
                            timeout));
                            }
                            catch (ReaderException ex)
                            {
                                if (ex is FAULT_NO_TAGS_FOUND_Exception)
                                {
                                    // just ignore "no tags found" response
                                }
                                else if (
                                    (ex is FAULT_SYSTEM_UNKNOWN_ERROR_Exception) ||
                                    (ex is FAULT_TM_ASSERT_FAILED_Exception))
                                {
                                    // real exception -- pass it on
                                    throw;
                                }
                                else if (ex is ReaderCodeException)
                                {
                                    // any other reader code exception like 0x504, 0x505
                                    notifyExceptionListeners(ex);
                                    //Get the left over tags in module tag buffer
                                    int tagCount = CmdGetTagsRemaining()[0];
                                    List<TagReadData> leftOverTags = GetAllTagReads(searchStart, tagCount, TagProtocol.NONE);
                                    tagReads.AddRange(leftOverTags);
                                    CmdClearTagBuffer();
                                    if (ex is FAULT_MSG_INVALID_PARAMETER_VALUE_Exception || ex is FAULT_AFE_NOT_ON_Exception)
                                    {
                                        throw ex;
                                    }
                                }
                                else if (ex is ReaderCommException)
                                {
                                    if (-1 != ex.Message.IndexOf("The operation has timed out."))
                                    {
                                        try
                                        {
                                            //Some serial drivers, such as USB ACM, don't return a 
                                            //disconnect exception on receive. So retry on send to get the correct exception.
                                            CheckIsPortAvailable();
                                        }
                                        catch (Exception ax)
                                        {
                                            throw ax;
                                        }
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                                else
                                {
                                    throw;
                                }
                            }
                            now = DateTime.Now;
                            if (isStopNTags)
                            {
                                break;
                            }
                            enableMultipleSelect = false;
                            isMultiFilterEnabled = false;
                            isEmbeddedTagOp = false;
                            isGen2AllMemoryBankEnabled = false;
                        }
                    }
                }
                else
                {
                    int asyncOffTime = (int)ParamGet("/reader/read/asyncOffTime");
                    foreach (ReadPlan r in mrp.Plans)
                    {
                        int subtimeout = 0;
                        if (0 == mrp.TotalWeight)
                        {
                            subtimeout = timeout / mrp.Plans.Length;
                            if (asyncOffTime != 0)
                            {
                                subOffTimeout = asyncOffTime / mrp.Plans.Length;
                            }
                        }
                        else
                        {
                            subtimeout = (int)timeout * r.Weight / mrp.TotalWeight;
                            if (asyncOffTime != 0)
                            {
                                subOffTimeout = (int)asyncOffTime * r.Weight / mrp.TotalWeight;
                            }
                        }
                        subtimeout = Math.Min(subtimeout, UInt16.MaxValue);
                        ReadInternal((UInt16)subtimeout, r, ref tagReads);
                        if (fetchTagReads)
                        {
                            OffTimeAdded = true;

                            QueueTagReads(tagReads);
                            timeEnd = DateTime.Now;
                            subOffTimeout = subOffTimeout - Convert.ToInt32(timeEnd.Subtract(timeStart).TotalMilliseconds);
                            if (subOffTimeout > 0)
                            {
                                Thread.Sleep(subOffTimeout);
                            }
                        }

                        // clear tag buffer after every read cycle
                        CmdClearTagBuffer();

                        if (fetchTagReads)
                        {
                            tagReads.Clear();
                        }
                    }
                }
                return;
            }
            else if ((rp is SimpleReadPlan) || (rp is StopTriggerReadPlan))
            {
                if (rp is StopTriggerReadPlan)
                {
                    StopTriggerReadPlan strp = (StopTriggerReadPlan)rp;
                    if (strp.stopOnCount is StopOnTagCount)
                    {
                        StopOnTagCount sotc = (StopOnTagCount)strp.stopOnCount;
                        isStopNTags = true;
                        numberOfTagsToRead = sotc.N;
                    }
                }

                SimpleReadPlan srp = (SimpleReadPlan)rp;
                if (srp.ReadTrigger != null)
                {
                    if (srp.ReadTrigger is GpiPinTrigger)
                    {
                        GpiPinTrigger gpiPinTrigger = (GpiPinTrigger)srp.ReadTrigger;
                        isTriggerReadEnable = gpiPinTrigger.enable;
                    }
                }

                //Fast search option
                isFastSearch = srp.UseFastSearch;

                /*If dynamic switching is enabled, API sets protocol with 0x93 and 0x01 option through /reader/protocolList param set
                 * but if it is not enabled , it fallbacks to older implementation of setting protocol through 0x93 without option from readplan.
                 */
                if (!isProtocolDynamicSwitching)
                {
                    if (supportedProtocols.Contains(srp.Protocol))
                    {
                        setProtocol(srp.Protocol);
                    }
                    else
                    {
                        throw new FAULT_INVALID_PROTOCOL_SPECIFIED_Exception();
                    }
                }
                else
                {
                    //Dynamic protocol switching is supported for async read in M3e
                    if ((model.Equals("M3e")) && (!enableStreaming))
                    {
                        isProtocolDynamicSwitching = false;
                        throw new Exception("Unsupported operation.");
                    }
                }

                if (isM6eVariant || (model.Equals("M3e")))
                {
                    //Disable ReadFilter in case of continuous Read
                    //and enable in case of sync read.
                    bool enableFilteringToSet = enableStreaming ? false : true;
                    if ((enableFilteringToSet != _enableFiltering) && (!userEnableReadFiltering))
                    {
                        CmdSetReaderConfiguration(Configuration.ENABLE_FILTERING, enableFilteringToSet);
                        _enableFiltering = enableFilteringToSet;
                    }
                }

                AntennaSelection antsel = PrepForSearch(srp, timeout);
                if (!model.Equals("M3e"))
                {
                    antsel |= AntennaSelection.LARGE_TAG_POPULATION_SUPPORT;
                }
                if (isFastSearch && (!enableStreaming))
                {
                    antsel |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_FAST_SEARCH;
                }

                if (isStopNTags)
                {
                    antsel |= AntennaSelection.READ_MULTIPLE_RETURN_ON_N_TAGS;
                }
                if (isTriggerReadEnable && (!enableStreaming))
                {
                    antsel |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAG_GPI_TRIGGER_READ;
                }

                if (isDutyCycleFlag && (!enableStreaming))
                {
                    antsel |= AntennaSelection.READ_MULTIPLE_SEARCH_DUTY_CYCLE_CONTROL;
                }
                DateTime now = DateTime.Now;
                DateTime endTime = now.AddMilliseconds(timeout);

                while (now <= endTime)
                {
                    DateTime tagFetchStartTime;
                    TimeSpan totalTagFetchTime = new TimeSpan();
                    TimeSpan timeElapsed = endTime - now;
                    timeout = ((ushort)timeElapsed.TotalMilliseconds < 65535) ? (ushort)timeElapsed.TotalMilliseconds : (ushort)65535;


                    DateTime searchStart = DateTime.Now;
                    // TODO: Does DateTime know about time zones?  Don't want things to break if data is shared worldwide.

                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    int accessPassword = (int)pwobj.Value;

                    List<byte> m = new List<byte>();

                    if (null == srp.Op) //no embedded command
                    {
                        if (enableStreaming)  //using streaming
                        {
                            List<SimpleReadPlan> srpList = new List<SimpleReadPlan>();
                            srpList.Add(srp);
                            CmdMultiProtocolSearch(CmdOpcode.TAG_READ_MULTIPLE, srpList, userMeta, antsel, timeout);
                            return;
                        }
                        else//no streaming
                        {
                            try
                            {
                                int tagCount = (int)CmdReadTagMultiple((ushort)timeout, antsel, srp.Filter, srp.Protocol);
                                tagReads.AddRange(GetAllTagReads(searchStart, tagCount, srp.Protocol));
                            }
                            catch (ReaderException ex)
                            {
                                if (ex is FAULT_NO_TAGS_FOUND_Exception)
                                {
                                    // just ignore "no tags found" response
                                }
                                else if (
                                    (ex is FAULT_SYSTEM_UNKNOWN_ERROR_Exception) ||
                                    (ex is FAULT_TM_ASSERT_FAILED_Exception))
                                {
                                    // real exception -- pass it on
                                    throw;
                                }
                                else if (ex is ReaderCodeException)
                                {
                                    tagFetchStartTime = DateTime.Now;
                                    //Get the left over tags in module tag buffer
                                    int tagCount = CmdGetTagsRemaining()[0];
                                    List<TagReadData> leftOverTags = GetAllTagReads(searchStart, tagCount, srp.Protocol);
                                    tagReads.AddRange(leftOverTags);
                                    CmdClearTagBuffer();
                                    totalTagFetchTime = DateTime.Now - tagFetchStartTime;

                                    // Any other reader code exception like 0x504, 0x505
                                    // M5e and its variants hardware does not have a real PA protection. So doing the
                                    // read without antenna may cause the damage to the reader. Hence stopping the
                                    // read in this case. It's okay to let M6e and its variants continue to operate
                                    // because it has a PA protection mechanism.
                                    if ((model != "M6e")
                                        && (model != "M6e Micro")
                                        && (model != "M6e Micro USB")
                                        && (model != "M6e Micro USBPro")
                                        && (model != "M6e PRC")
                                        && (model != "M6e Nano")
                                        && (model != "M6e JIC")
                                        && (ex is FAULT_AHAL_HIGH_RETURN_LOSS_Exception))
                                    {
                                        throw ex;
                                    }
                                    if (ex is FAULT_MSG_INVALID_PARAMETER_VALUE_Exception || ex is FAULT_AFE_NOT_ON_Exception)
                                    {
                                        throw ex;
                                    }
                                    notifyExceptionListeners(ex);
                                }
                                else if (ex is ReaderCommException)
                                {
                                    if (-1 != ex.Message.IndexOf("The operation has timed out."))
                                    {
                                        try
                                        {
                                            //Some serial drivers, such as USB ACM, don't return a 
                                            //disconnect exception on receive. So retry on send to get the correct exception.
                                            CheckIsPortAvailable();
                                        }
                                        catch (Exception ax)
                                        {
                                            throw ax;
                                        }
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                    }
                    else //embedded command
                    {
                        antsel |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_EMBEDDED_OP;

                        if (!(model.Equals("M3e")))
                        {
                            if (srp.Protocol != TagProtocol.GEN2)
                                throw new ReaderException("The embedded command supports Gen2 and all M3e supported protocols");
                        }

                        if (enableStreaming)  //using streaming
                        {
                            List<SimpleReadPlan> srpList = new List<SimpleReadPlan>();
                            srpList.Add(srp);
                            CmdMultiProtocolSearch(CmdOpcode.TAG_READ_MULTIPLE, srpList, userMeta, antsel, timeout);
                        }
                        else  //non-streaming
                        {
                            try
                            {
                                userMeta = (TagMetadataFlag)ParamGet("/reader/metadata");
                                msgAddEmbeddedReadOp(ref m, timeout, antsel, srp.Filter, srp.Protocol, userMeta, accessPassword, srp);
                                UInt32 tagCount = executeEmbeddedRead(m, (ushort)timeout);
                                List<TagReadData> reads = GetAllTagReads(searchStart, (int)tagCount, srp.Protocol);
                                tagReads.AddRange(reads);
                            }
                            catch (ReaderException ex)
                            {
                                if (ex is FAULT_NO_TAGS_FOUND_Exception)
                                {
                                    // just ignore "no tags found" response
                                }
                                else if (
                                    (ex is FAULT_SYSTEM_UNKNOWN_ERROR_Exception) ||
                                    (ex is FAULT_TM_ASSERT_FAILED_Exception) ||
                                    (ex is FAULT_UNIMPLEMENTED_FEATURE_Exception))
                                {
                                    // real exception -- pass it on
                                    throw;
                                }
                                else if (ex is ReaderCodeException)
                                {
                                    //Get the left over tags in module tag buffer
                                    int tagCount = CmdGetTagsRemaining()[0];
                                    List<TagReadData> leftOverTags = GetAllTagReads(searchStart, tagCount, srp.Protocol);
                                    tagReads.AddRange(leftOverTags);
                                    CmdClearTagBuffer();

                                    // Any other reader code exception like 0x504, 0x505
                                    // M5e and its variants hardware does not have a real PA protection. So doing the
                                    // read without antenna may cause the damage to the reader. Hence stopping the
                                    // read in this case. It's okay to let M6e and its variants continue to operate
                                    // because it has a PA protection mechanism.
                                    if ((model != "M6e")
                                        && (model != "M6e Micro")
                                        && (model != "M6e Micro USB")
                                        && (model != "M6e Micro USBPro")
                                        && (model != "M6e PRC")
                                        && (model != "M6e Nano")
                                        && (model != "M6e JIC")
                                        && (ex is FAULT_AHAL_HIGH_RETURN_LOSS_Exception))
                                    {
                                        throw ex;
                                    }
                                    if (ex is FAULT_MSG_INVALID_PARAMETER_VALUE_Exception)
                                    {
                                        throw ex;
                                    }
                                    notifyExceptionListeners(ex);
                                }
                                else if (ex is ReaderCommException)
                                {
                                    if (-1 != ex.Message.IndexOf("The operation has timed out."))
                                    {
                                        try
                                        {
                                            //Some serial drivers, such as USB ACM, don't return a 
                                            //disconnect exception on receive. So retry on send to get the correct exception.
                                            CheckIsPortAvailable();
                                        }
                                        catch (Exception ax)
                                        {
                                            throw ax;
                                        }
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                    }

                    m = null;
                    now = DateTime.Now - totalTagFetchTime;
                    if (isStopNTags)
                    {
                        break;
                    }
                }
                isEmbeddedTagOp = false;
                enableMultipleSelect = false;
            }
            else
                throw new NotSupportedException("Unsupported read plan: " + rp.GetType().ToString());

            /*deduplication*/
            if (_enableFiltering)
            {
                Dictionary<string, TagReadData> dic = new Dictionary<string, TagReadData>();

                List<TagReadData> _tagReads = new List<TagReadData>();
                string key;

                foreach (TagReadData tag in tagReads)
                {
                    key = tag.EpcString;

                    if (uniqueByData)
                    {
                        key += ";" + ByteFormat.ToHex(tag.Data, "", "");
                    }
                    if (uniqueByAntenna)
                    {
                        key += ";" + tag.Antenna.ToString();
                    }
                    if (uniqueByProtocol)
                    {
                        key += ";" + tag.Tag.Protocol.ToString();
                    }
                    if (!dic.ContainsKey(key))
                    {
                        dic.Add(key, tag);

                    }
                    else  //see the tag again
                    {
                        dic[key].ReadCount += tag.ReadCount;
                        if (isRecordHighestRssi)
                        {
                            if (tag.Rssi > dic[key].Rssi)
                            {
                                int tmp = dic[key].ReadCount;
                                dic[key] = tag;
                                dic[key].ReadCount = tmp;
                            }
                        }

                    }
                }
                tagReads.Clear();
                tagReads.AddRange(dic.Values);
            }
        }
        /// <summary>
        /// checking serial port status by sending version command
        /// </summary>
        private void CheckIsPortAvailable()
        {
            byte[] data = new byte[] { 0x03 };
            GetDataFromM5eResponse(SendTimeout(data, 0));
        }

        /// <summary>
        /// Compare antenna list in readplans list, return true if antenna
        /// list are consistent across the entire set of read plans.
        /// </summary>
        /// <param name="readPlanList"> Accepts readplans list</param>
        /// <returns>True, if antennalist are consistent across the entire
        /// set of read plans else return false </returns>
        private bool CompareAntennas(ReadPlan[] readPlanList)
        {
            int allAntennasNull = 0;
            int noAntennasNull = 0;
            bool status = false;
            for (int index = 0; index < readPlanList.Length; index++)
            {
                SimpleReadPlan plan1 = (SimpleReadPlan)readPlanList[0];
                SimpleReadPlan plan2 = (SimpleReadPlan)readPlanList[index];
                if ((plan1.Antennas != null) && (plan2.Antennas != null))
                {
                    if (false == ArrayEquals<int>(plan1.Antennas, plan2.Antennas))
                    {
                        status = false;
                        break;
                    }
                    ++noAntennasNull;
                }
                else if ((plan1.Antennas == null) && (plan2.Antennas == null))
                {
                    ++allAntennasNull;
                }
                else
                {
                    status = false;
                    break;
                }
            }
            if ((noAntennasNull == readPlanList.Length) || (allAntennasNull == readPlanList.Length))
            {
                status = true;
            }
            return status;
        }
        private bool validateParams(MultiReadPlan multiReadPlan)
        {
            bool status = true;
            SimpleReadPlan plan1;
            SimpleReadPlan plan2;
            int matchingPlanCount = 0;
            for (int index = 0; index < multiReadPlan.Plans.Length - 1; index++)
            {
                plan1 = (SimpleReadPlan)multiReadPlan.Plans[0];
                plan2 = (SimpleReadPlan)multiReadPlan.Plans[index + 1];
                if (
                    (plan1.Protocol == plan2.Protocol) &&
                    (((plan1.Filter == null) && (plan2.Filter == null)) || (plan1.Filter == plan2.Filter)) &&
                    (((plan1.Op == null) && (plan2.Op == null)) || (plan1.Op == plan2.Op))
                    )
                {
                    matchingPlanCount++;
                }
                else
                {
                    status = false;
                    break;
                }
            }
            if (matchingPlanCount == (multiReadPlan.Plans.Length - 1))
            {
                status = true;
            }
            return status;
        }
        private bool validateMultiReadPlan(MultiReadPlan mrp)
        {
            if (validateParams(mrp) && (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ANTENNA_READ_TIME)))
            {
                isValidationSuccess = true;
            }
            else
            {
                isValidationSuccess = CompareAntennas(mrp.Plans);
            }
            return isValidationSuccess;
        }
        private void receiveBufferedReads()
        {
            bool keepReceiving = true;
            byte[] response = default(byte[]);
            while (keepReceiving)
            {
                try
                {
                    response = receiveMessage((byte)0x2f, 0);
                    // Stop flushing the bytes in buffer only after getting the stop read response.
                    // Here 0x2F is Opcode and 0x02 specifies stop read response.
                    if (response[2] == 0x2F && response[5] == 0x02)
                    {
                        keepReceiving = false;
                        enableMultipleSelect = false;

                        try
                        {
                            transportType = (TransportType)Enum.Parse(typeof(TransportType), CmdGetReaderConfiguration(Configuration.CURRENT_MESSAGE_TRANSPORT).ToString(), true);
                            if (transportType.Equals(TransportType.SOURCEUSB))
                            {
                                try
                                {
                                    CmdSetReaderConfiguration(Configuration.SEND_CRC, false);
                                    isCRCEnabled = false;
                                }
                                catch (ReaderException)
                                {
                                    //Ignoring unsupported exception with old firmware and latest API
                                }
                            }
                            //adding else-if part when transportType is SOURCESERIAL and enabling CRC.
                            else if (transportType.Equals(TransportType.SOURCESERIAL))
                            {
                                try
                                {
                                    CmdSetReaderConfiguration(Configuration.SEND_CRC, true);
                                    isCRCEnabled = true;
                                }
                                catch (ReaderException)
                                {
                                    //Ignoring unsupported exception with old firmware and latest API
                                }

                            }
                        }
                        catch (ReaderCodeException)
                        {
                            //Not fatal, going with crc enabled
                        }
                    }
                }
                catch (ReaderException ex)
                {
                    if (-1 != ex.Message.IndexOf("CRC Error"))
                    {
                        //When the module is pushing all tags out, just don't bother about
                        //CRC and keep waiting for response for stopReading
                        continue;
                    }
                    else if (-1 != ex.Message.IndexOf("Invalid M6e response header, SOH not found in response"))
                    {
                        throw ex;
                    }
                    else if (-1 != ex.Message.IndexOf("Autonomous mode is enabled on reader. Please disable it"))
                    {
                        notifyExceptionListeners(new ReaderException("Autonomous mode is enabled on reader. Please disable it."));
                        return;
                    }
                }
            }
        }
        ManualResetEvent waitForStopResponseEvent = new ManualResetEvent(false);
        private void ReceiveResponseStream()
        {
            bool keepReceiving = true;
            byte[] response = default(byte[]);
            while (keepReceiving)
            {
                try
                {
                    if (baseTime == DateTime.MinValue)
                    {
                        //Update the base time with host time at the time start receiving data.
                        baseTime = DateTime.Now;
                    }
                    int timeout = (int)ParamGet("/reader/read/asyncOnTime");
                    response = receiveMessage((byte)CmdOpcode.TAG_READ_MULTIPLE, timeout);
                    // Detect end of stream
                    if ((response != null) && (response[2] == 0x2f) && (response[5] != 0x01))
                    {
                        if (response[5] == 0x04)
                        {
                            if (paramWait)
                            {
                                paramMessage.Clear();
                                paramMessage.AddRange(response);
                                paramWait = false;
                                break;
                            }
                        }
                        else
                        {
                            continuousReadActive = false;
                            hasContinuousReadStarted = false;
                            keepReceiving = false;
                            baseTime = DateTime.MinValue;
                            waitForStopResponseEvent.Set();
                        }
                    }
                    else if ((response[5] != 0x01) && (response[2] != 0x2f) && (response[2] != 0x9d))
                    {
                        // Excluding the process of the response containing 9D and 2F opcode.
                        ProcessStreamingResponse(response);
                    }
                    #region Stop on N Tag read response Parsing
                    else if ((response[2] == 0x22) && (response[1] == 0x07) && (response[3] == 0x00) && (response[4] == 0x00))
                        {
                            int option = ByteConv.GetU8(response, 5);
                            int searchFlag = ByteConv.ToU16(response, 6);
                            //Check if Stop N tag feature is enabled.
                            if ((0x01 & option) == 0x01 && (isStopNTags && ((searchFlag & (int)AntennaSelection.READ_MULTIPLE_RETURN_ON_N_TAGS) == (int)AntennaSelection.READ_MULTIPLE_RETURN_ON_N_TAGS)))
                            {
                                //Retrieve total tag count read.
                                uint tagCount = ByteConv.ToU32(response, 8);

                                //Check if total requested tag count is matching with total tag read count.
                                if (tagCount >= numberOfTagsToRead)
                                {
                                    CmdStopReading();
                                    //_exitNow = true;
                                    receiveMessage(0x2F, 0);
                                    _exitNow = true;
                                    keepReceiving = false;
                                    continuousReadActive = false;
                                    hasContinuousReadStarted = false;
                                    finishedReading = true;
                                    isReadInitiated = false;
                                }
                            }
                        }
                    #endregion

                    }
                catch (ReaderException ex)
                {
                    // Notify exception listener
                    if (!((ex is FAULT_NO_TAGS_FOUND_Exception) || (ex is FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception) || (ex is ReaderCommException)))
                    {
                        ReadExceptionPublisher expub = new ReadExceptionPublisher(this, ex);
                        Thread trd = new Thread(expub.OnReadException);
                        trd.Start();
                    }

                    if (ex is ReaderCommException)
                    {
                        if (ex.Message.Equals("CRC Error"))
                        {
                            notifyExceptionListeners(ex);
                        }
                        else if (ex.Message.ToLower().ToString() == "buffer overflow")
                        {
                            notifyExceptionListeners(ex);
                            try
                            {
                                CmdStopReading();
                                receiveBufferedReads();
                                receiveMessage(0x2F, 0);
                            }
                            catch (Exception)
                            { }
                            while (_backgroundNotifierCallbackCount > 0)
                            { }
                            _exitNow = false;
                            keepReceiving = false;
                            continuousReadActive = false;
                            hasContinuousReadStarted = false;
                        }
                        else
                        {
                            // Handle Comm exceptions.
                            notifyExceptionListeners(ex);
                            keepReceiving = false;
                            continuousReadActive = false;
                            hasContinuousReadStarted = false;
                            waitForStopResponseEvent.Set();
                            try
                            {
                                _serialPort.Flush();
                                if (!_exitNow)
                                {
                                    CmdStopReading();
                                }
                            }
                            catch
                            {
                            }
                            if (!_exitNow)
                            {
                                _exitNow = true;
                            }
                        }
                    }

                    // If buffer overflow, resend command to restart search
                    // If transient error, keep going
                    // For all other errors, send stop command to abort search
                    else if (ex is ReaderCodeException)
                    {
                        ReaderCodeException rce = (ReaderCodeException)ex;
                        int status = rce.Code;

                        if (ex is FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception)
                        {
                            // Tag password needed to complete tagop.
                            // Parse TagReadData and pass to password-generating callback,
                            // which will return the appropriate authentication.
                            TagReadData t = new TagReadData();
                            byte[] authRequestResponse = ((FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception)ex).ReaderMessage;
                            int offSet = enableMultipleSelect ? 7 : 6;
                            int readOffset = ARGS_RESPONSE_OFFSET + offSet; //start of metadata response[11]
                            TagProtocol protocol = TagProtocol.NONE;
                            int index = enableMultipleSelect ? 9 : 8;
                            metadataFlags = (TagMetadataFlag)ByteConv.ToU16(authRequestResponse, index);
                            ParseTagMetadata(ref t, authRequestResponse, ref readOffset, metadataFlags, ref protocol);
                            t._tagData = ParseTagData(authRequestResponse, ref readOffset, protocol, 0);
                            t.Reader = this;
                            Gen2.Password accessPassword = null;
                            try
                            {
                                // Fire reader authentication event with tag read data.
                                OnReadAuthentication(t);
                                // Get the secure accesspassword corresponding with tag epc
                                accessPassword = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                                UInt32 password = accessPassword.Value;
                                // Send new message to tag with secure accesspassword corresponding with tag epc 
                                hasContinuousReadStarted = false;
                                CmdAuthReqResponse(password);
                                if (isTrueContinuousRead)
                                    hasContinuousReadStarted = true;
                            }
                            catch (Exception rdAuthException)
                            {
                                ReadExceptionPublisher expubrdAuth = new ReadExceptionPublisher(this, new ReaderException(rdAuthException.Message));
                                Thread trdexpubrdAuth = new Thread(expubrdAuth.OnReadException);
                                trdexpubrdAuth.Start();
                            }
                        }
                        else if (ex is FAULT_TAG_ID_BUFFER_FULL_Exception)
                        {
                            notifyExceptionListeners(ex);
                            if (_exitNow)
                            {
                                try
                                {
                                    receiveMessage(0x2F, 0);
                                }
                                catch
                                {
                                    //StopReading is called. Receive this response as well
                                }
                                continuousReadActive = false;
                                hasContinuousReadStarted = false;
                                keepReceiving = false;
                                waitForStopResponseEvent.Set();
                            }
                            else
                            {
                                // Exit our loop to allow caller loop to retry its Read call
                                keepReceiving = false;
                                // Clear flag to allow "start read" command to be sent again
                                continuousReadActive = false;
                                hasContinuousReadStarted = false;

                                // Don't need to explicitly resend "start continuous read" here, because our caller
                                // has a loop that does it until _exitNow is explicitly commanded by the user.
                            }
                        }
                        else if ((0x0400 == (status & 0xFF00))
                            || (ex is FAULT_AHAL_HIGH_RETURN_LOSS_Exception)
                            || (ex is FAULT_AHAL_CHANNEL_OCCUPIED_Exception)
                            || (ex is FAULT_AHAL_TEMPERATURE_EXCEED_LIMITS_Exception)
                            )
                        {
                            // Continue
                            keepReceiving = true;
                        }
                        else if (ex.Message.Equals("The reader received a valid command with an unsupported or invalid parameter"))
                        {
                            notifyExceptionListeners(ex);
                        }
                        else
                        {
                            // Abort
                            keepReceiving = false;
                            continuousReadActive = false;
                            hasContinuousReadStarted = false;
                            waitForStopResponseEvent.Set();
                            if (!_exitNow)
                            {
                                StopReading();
                            }
                        }
                    }
                    else
                    {
                        notifyExceptionListeners(ex);
                    }
                }
                catch (Exception ex)
                {
                    ReadExceptionPublisher expub = new ReadExceptionPublisher(this, new ReaderException(ex.Message));
                    Thread trd = new Thread(expub.OnReadException);
                    trd.Start();

                    keepReceiving = false;
                    continuousReadActive = false;
                    hasContinuousReadStarted = false;
                    waitForStopResponseEvent.Set();
                }
            }
        }

        private void ProcessStreamingResponse(byte[] response)
        {
            try
            {
                if (null != response)
                {
                    // To avoid index was outside the bounds of the array error.
                    if (response.Length >= 8)
                    {
                        byte responseType;
                        if (enableMultipleSelect || enableReadAfterWrite)
                        {
                            responseType = response[((response[6] & (byte)0x10) == 0x10) ? 11 : 9];
                        }
                        else if (autonomousStreaming)
                        {
                            if (model == "M3e")
                            {
                                responseType = response[((response[5] & (byte)0x10) == 0x10) ? 10 : 8];
                            }
                            else
                            {
                                responseType = response[((response[6] & (byte)0x10) == 0x10) ? 11 : 9];
                            }
                        }
                        else
                        {
                            responseType = response[((response[5] & (byte)0x10) == 0x10) ? 10 : 8];
                        }
                        BoolResponse br = new BoolResponse();
                        ParseResponseByte(responseType, br);
                        if (br.parseResponse)
                        {
                            if (br.statusResponse)
                            {
                                if (statFlag == Stat.StatsFlag.NONE)
                                {
                                    // Requested for reader statistics
                                    List<StatusReport> sreports = new List<StatusReport>();
                                    int offSet = ARGS_RESPONSE_OFFSET + 4;
                                    int contentFlags = ByteConv.ToU16(response, ref offSet); // content flags

                                    if (0 != (contentFlags & (byte)ReaderStatusFlag.FREQUENCY))
                                    {
                                        FrequencyStatusReport fsr = new FrequencyStatusReport();
                                        fsr.Frequency = ByteConv.GetU24(response, offSet);
                                        offSet += 3;
                                        sreports.Add(fsr);
                                    }
                                    if (0 != (contentFlags & (byte)ReaderStatusFlag.TEMPERATURE))
                                    {
                                        TemperatureStatusReport tsr = new TemperatureStatusReport();
                                        tsr.Temperature = ByteConv.GetU8(response, offSet);
                                        offSet++;
                                        sreports.Add(tsr);
                                    }
                                    if (0 != (contentFlags & (byte)ReaderStatusFlag.CURRENT_ANTENNAS))
                                    {
                                        AntennaStatusReport asr = new AntennaStatusReport();
                                        byte TxPort = (byte)(ByteConv.GetU8(response, offSet));
                                        offSet++;
                                        byte RxPort = (byte)(ByteConv.GetU8(response, offSet));
                                        asr.Antenna = _txRxMap.TranslateSerialAntenna((byte)((TxPort << 4) | RxPort));
                                        sreports.Add(asr);
                                    }
                                    OnStatusRead(sreports.ToArray());
                                }
                                else
                                {
                                    // Requested for reader stats
                                    int offSet;
                                    if (enableMultipleSelect)
                                    {
                                        offSet = ARGS_RESPONSE_OFFSET + 5;
                                    }
                                    else if (autonomousStreaming)
                                    {
                                        if (model == "M3e")
                                        {
                                            offSet = ARGS_RESPONSE_OFFSET + 4;
                                        }
                                        else
                                        {
                                            offSet = ARGS_RESPONSE_OFFSET + 5;
                                        }
                                    }
                                    else
                                    {
                                        offSet = ARGS_RESPONSE_OFFSET + 4;
                                    }
                                    /* Get status content flags */
                                    if ((0x80) > statusFlags)
                                    {
                                        offSet += 1;
                                    }
                                    else
                                    {
                                        offSet += 2;
                                    }
                                    ReaderStatsReport statusreport = new ReaderStatsReport();
                                    if (transportType.Equals(TransportType.SOURCEUSB))
                                    {
                                        statusreport.STATS = ParseReaderStatValues(response, offSet, Convert.ToUInt16(statFlag));
                                    }
                                    else
                                    {
                                        byte[] tempResponse = new byte[response.Length - 2];
                                        Array.Copy(response, 0, tempResponse, 0, response.Length - 2);
                                        statusreport.STATS = ParseReaderStatValues(tempResponse, offSet, Convert.ToUInt16(statFlag));
                                    }
                                    OnStatsRead(statusreport);
                                }
                                br.statusResponse = false;
                            }
                            else
                            {
                                //to solve negative time stamp issue. elapsed time is added to baseTime when no tag found.
                                if ((((response[5] & 0x88) == (byte)SINGULATION_OPTION_MULTIPLE_SELECT || (model.Equals("M3e"))) && (response[3] == 0x04) && (response[4] == 0x00)))
                                {
                                    Double elapsedTime = 0;
                                    if (model == ("M3e"))
                                    {
                                        elapsedTime = Convert.ToDouble(ByteConv.ToU32(response, 11));
                                    }
                                    else
                                    {
                                        elapsedTime = Convert.ToDouble(ByteConv.ToU32(response, 12));
                                    }
                                    baseTime = DateTime.Now;
                                    return;
                                }
                                TagReadData t = new TagReadData();
                                t._baseTime = baseTime;
                                TagProtocol protocol = TagProtocol.NONE;
                                int readOffset;
                                if (enableMultipleSelect || enableReadAfterWrite)
                                {
                                    readOffset = ARGS_RESPONSE_OFFSET + 7; //start of metadata response[12]
                                    metadataFlags = (TagMetadataFlag)ByteConv.ToU16(response, 9);
                                }
                                else if (autonomousStreaming)
                                {
                                    if (model == "M3e")
                                    {
                                        readOffset = ARGS_RESPONSE_OFFSET + 6; //start of metadata response[11]                                    
                                        metadataFlags = (TagMetadataFlag)ByteConv.ToU16(response, 8);
                                    }
                                    else
                                    {
                                        readOffset = ARGS_RESPONSE_OFFSET + 7; //start of metadata response[12]
                                        metadataFlags = (TagMetadataFlag)ByteConv.ToU16(response, 9);
                                    }
                                }
                                else
                                {
                                    readOffset = ARGS_RESPONSE_OFFSET + 6; //start of metadata response[11]                                    
                                    metadataFlags = (TagMetadataFlag)ByteConv.ToU16(response, 8);
                                }
                                ParseTagMetadata(ref t, response, ref readOffset, metadataFlags, ref protocol);
                                t._tagData = ParseTagData(response, ref readOffset, protocol, 0);
                                //Checking if last tag's timestamp is greater than current tag's timestamp
                                if (LastTagTime > t.Time)
                                {
                                    TimeSpan diff = LastTagTime - t.Time;
                                    t._baseTime = t._baseTime.Add(diff);
                                }
                                LastTagTime = t.Time;

                                // Handling NegativeArraySizeException
                                // Ignoring invalid tag response (epcLen goes to negative), which disturbs further parsing of tagresponse
                                if (t._tagData._crc != null || model.Equals("M3e"))
                                {
                                    hasTagReportCleared = true;
                                    QueueTagReads(new TagReadData[] { t });
                                }
                            }
                        }
                        else
                        {
                            // Update base time with host time on each read cycle
                            baseTime = DateTime.Now;
                            SimpleReadPlan srp = null;
                            MultiReadPlan mrp = null;
                            TagOp tagop = null;
                            try
                            {
                                ReadPlan rp = (ReadPlan)ParamGet("/reader/read/plan");
                                if (rp is SimpleReadPlan)
                                {
                                    srp = (SimpleReadPlan)rp;
                                    tagop = srp.Op;
                                }
                                else
                                {
                                    mrp = (MultiReadPlan)rp;
                                    tagop = ((SimpleReadPlan)mrp.Plans[0]).Op;
                                }
                            }
                            catch (ReaderException)
                            {
                                //break;
                            }
                            if (null != tagop)
                            {
                                if (response[02] != 0x2f)
                                {
                                    tagOpSuccessCount += ByteConv.ToU16(response, 15);
                                    tagOpFailuresCount += ByteConv.ToU16(response, 17);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Update base time with host time on each read cycle
                        baseTime = DateTime.Now;
                        //Don't catch TMR_ERROR_NO_TAGS_FOUND for ReadTagMultiple.
                        if (ByteConv.ToU16(response, 3) != FAULT_NO_TAGS_FOUND_Exception.StatusCode)
                        {
                            // In case of streaming and ISO protocol after every
                            // search cycle module sends the response for 
                            // embedded operation status as FF 00 22 00 00. In
                            // this case return back with 0x400 error, because 
                            // success response will deceive the read thread 
                            // to process it. For GEN2 case we got the response
                            // with 0x400 status.

                            if (ByteConv.ToU16(response, 3) == 0 && response[1] == 0)
                            {
                                //do nothing
                            }
                            else
                            {
                                ReadExceptionPublisher expub = new ReadExceptionPublisher(this, new ReaderException("Unable to parse data"));
                                Thread trd = new Thread(expub.OnReadException);
                                trd.Start();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ReadExceptionPublisher expub = new ReadExceptionPublisher(this, new ReaderException(ex.Message));
                Thread trd = new Thread(expub.OnReadException);
                trd.Start();
                if (ex is ReaderCommException)
                {
                    throw ex;
                }
            }
        }
        private void AssignStatusFlags()
        {
            if (statFlag == Stat.StatsFlag.NONE)
            {
                if (frequencyStatusEnable)
                {
                    statusFlags |= 0x0002;
                }
                if (temperatureStatusEnable)
                {
                    statusFlags |= 0x0004;
                }
                if (antennaStatusEnable)
                {
                    statusFlags |= 0x0008;
                }
            }
            else
            {
                statusFlags = Convert.ToInt16(statFlag); ;
            }
        }

        #endregion

        /// <summary>
        /// Class for wheather to parse the status or response
        /// </summary>
        private class BoolResponse
        {
            internal bool parseResponse = true;
            internal bool statusResponse = false;
        }
        /// <summary>
        /// Internal method to parse response byte in the 22h command response
        /// </summary>
        /// <param name="responseByte">responseByte</param>
        /// <param name="br"></param>
        /// <returns>true/false</returns>
        private bool ParseResponseByte(byte responseByte, BoolResponse br)
        {
            bool response = true;
            switch (responseByte)
            {
                case 0x02:
                    //mid stream status response
                    br.statusResponse = true;
                    break;
                case 0x01:
                    // mid stream tag buffer response
                    break;
                case 0x00:
                    // final stream response
                    br.parseResponse = false;
                    break;
            }
            return response;
        }

        private int prepEmbReadTagMultiple(ref List<byte> m, UInt16 timeout, AntennaSelection antennas, TagFilter filt, TagProtocol protocol, TagMetadataFlag metadataFlags, int accessPassword, SimpleReadPlan srp)
        {
            m = msgSetupReadTagMultiple((ushort)timeout, antennas, srp.Filter, srp.Protocol, userMeta, accessPassword);

            /*assembly embedded command*/
            m.Add(0x01); //embedded cmd count,currently only supporting 1
            return m.LastIndexOf(0x01) + 1; //record the index of the embedded command length byte
        }

        private void msgAddEmbeddedReadOp(ref List<byte> m, UInt16 timeout, AntennaSelection antennas, TagFilter filt, TagProtocol protocol, TagMetadataFlag metadataFlags, int accessPassword, SimpleReadPlan srp)
        {
            byte embedLen = 0;//length of embedded cmd
            int tm = 0;
            isEmbeddedTagOp = true;
            if (srp.Op is Gen2.SecureReadData)
            {
                isSecureAccessEnabled = true;
                if ((((Gen2.SecureReadData)srp.Op).password is Gen2.SecurePasswordLookup))
                {
                    isSecurePasswordLookupEnabled = true;
                    List<byte> APAddress = new List<byte>();
                    Gen2.SecurePasswordLookup passwordLookUp = (Gen2.SecurePasswordLookup)((Gen2.SecureReadData)srp.Op).password;
                    APAddress.Add(passwordLookUp.SecureAddressLength);
                    APAddress.Add(passwordLookUp.SecureAddressOffset);
                    byte[] offset = new byte[2];
                    ByteConv.FromU16(offset, 0, passwordLookUp.SecureFlashOffset);
                    APAddress.AddRange(offset);
                    accessPassword = (int)ByteConv.ToU32(APAddress.ToArray(), 0);
                }
                else if (((Gen2.SecureReadData)srp.Op).password is Gen2.Password)
                {
                    accessPassword = (int)((Gen2.Password)((Gen2.SecureReadData)srp.Op).password).Value;
                }
                else
                {
                    throw new ArgumentException("Invalid password type");
                }
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2DataRead(ref m, (ushort)timeout, ((Gen2.ReadData)(srp.Op)).Bank, ((Gen2.ReadData)(srp.Op)).WordAddress, ((Gen2.ReadData)(srp.Op)).Len, ((Gen2.SecureReadData)(srp.Op)).type);
                isSecurePasswordLookupEnabled = false;
            }
            else if (srp.Op is Gen2.ReadData)
            {
                Gen2.ReadData Operation = (Gen2.ReadData)(srp.Op);
                //If the user wants to read all memory bank data, in that case the operation.bank value should be greater then 3
                if ((int)Operation.Bank > 3)
                {
                    isGen2AllMemoryBankEnabled = true;
                }
                uint wordAddress = ((Gen2.ReadData)(srp.Op)).WordAddress;
                byte wordCount = ((Gen2.ReadData)(srp.Op)).Len;
                // Zero length read for M5e variants is not supported
                if ((wordCount == 0) && M5eFamilyList.Contains(model))
                {
                    throw new ReaderException(
                        "Operation not supported. M5e does not support zero-length read.");
                }
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol,
                    metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2DataRead(ref m, (ushort)timeout,
                    ((Gen2.ReadData)(srp.Op)).Bank, wordAddress, wordCount,
                    Gen2.SecureTagType.DEFAULT);
            }
            else if (srp.Op is Gen2.WriteData)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2DataWrite(ref m, (ushort)timeout, ((Gen2.WriteData)(srp.Op)).Bank, ((Gen2.WriteData)(srp.Op)).WordAddress, ((Gen2.WriteData)(srp.Op)).Data);
            }
            else if (srp.Op is TagOpList)
            {
                ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                TagOpList tagOpList = (TagOpList)srp.Op;
                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                UInt32 password = pwobj.Value;
                if (tagOpList.list.Count == 1)
                {
                    TagOp op = tagOpList.list[0];
                    msgAddEmbeddedReadOp(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                }
                else if (tagOpList.list.Count == 2)
                {
                    byte[] bytes = new byte[] { };
                    ushort[] words = new ushort[] { };
                    if ((tagOpList.list[0] is Gen2.WriteData) && (tagOpList.list[1] is Gen2.ReadData))
                    {
                        enableReadAfterWrite = true;
                        Gen2.WriteData wdata = (Gen2.WriteData)tagOpList.list[0];
                        Gen2.ReadData rdata = (Gen2.ReadData)tagOpList.list[1];
                        tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                        embedLen = msgAddGEN2DataWrite(ref m, (ushort)timeout, (Gen2.Bank)wdata.Bank, (UInt32)wdata.WordAddress, wdata.Data);
                        m.Add((byte)rdata.Bank);
                        m.AddRange(ByteConv.EncodeU32(rdata.WordAddress));
                        m.Add(rdata.Len);
                        int length = Convert.ToInt16(embedLen);
                        length = length + 6;
                        embedLen = Convert.ToByte(length);
                    }
                    else if ((tagOpList.list[0] is Gen2.WriteTag) && (tagOpList.list[1] is Gen2.ReadData))
                    {
                        enableReadAfterWrite = true;
                        Gen2.WriteTag wtag = (Gen2.WriteTag)tagOpList.list[0];
                        Gen2.ReadData rdata = (Gen2.ReadData)tagOpList.list[1];
                        tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                        embedLen = msgAddGEN2WriteTagEPC(ref m, (ushort)timeout, wtag.Epc);
                        m.Add((byte)rdata.Bank);
                        m.AddRange(ByteConv.EncodeU32(rdata.WordAddress));
                        m.Add(rdata.Len);
                        int length = Convert.ToInt16(embedLen);
                        length = length + 6;
                        embedLen = Convert.ToByte(length);
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("Operation not supported");
                    }
                }
                else
                {
                    throw new FeatureNotSupportedException("Operation not supported");
                }

            }
            else if (srp.Op is Gen2.Lock)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2LockTag(ref m, (ushort)timeout, ((Gen2.Lock)(srp.Op)).AccessPassword, ((Gen2.Lock)(srp.Op)).LockAction.Mask, ((Gen2.Lock)(srp.Op)).LockAction.Action);
            }
            else if (srp.Op is Gen2.Kill)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2KillTag(ref m, (ushort)timeout, ((Gen2.Kill)(srp.Op)).KillPassword);
            }
            else if (srp.Op is Gen2.BlockWrite)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2BlockWrite(ref m, (ushort)timeout, ((Gen2.BlockWrite)(srp.Op)).Bank, ((Gen2.BlockWrite)(srp.Op)).WordPtr, ((Gen2.BlockWrite)(srp.Op)).Data, 0, null);
            }
            else if (srp.Op is Gen2.BlockPermaLock)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2BlockPermaLock(ref m, (ushort)timeout, ((Gen2.BlockPermaLock)(srp.Op)).ReadLock, ((Gen2.BlockPermaLock)(srp.Op)).Bank, ((Gen2.BlockPermaLock)(srp.Op)).BlockPtr, ((Gen2.BlockPermaLock)(srp.Op)).BlockRange, ((Gen2.BlockPermaLock)(srp.Op)).Mask, 0, null);
            }
            else if (srp.Op is Gen2.BlockErase)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddEraseBlockTagSpecific(ref m, (ushort)timeout, ((Gen2.BlockErase)(srp.Op)).Bank, ((Gen2.BlockErase)(srp.Op)).WordPtr, ((Gen2.BlockErase)(srp.Op)).WordCount, (UInt32)accessPassword, null);
            }
            else if (srp.Op is Gen2.WriteTag)
            {
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddGEN2WriteTagEPC(ref m, (ushort)timeout, ((Gen2.WriteTag)(srp.Op)).Epc);
            }
            else if (srp.Op is Gen2.Alien.Higgs2.PartialLoadImage)
            {
                Gen2.Alien.Higgs2.PartialLoadImage partialLoadImageOp = (Gen2.Alien.Higgs2.PartialLoadImage)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddHiggs2PartialLoadImage(ref m, timeout, partialLoadImageOp.AccessPassword, partialLoadImageOp.KillPassword, partialLoadImageOp.Epc, null);
            }
            else if (srp.Op is Gen2.Alien.Higgs2.FullLoadImage)
            {
                Gen2.Alien.Higgs2.FullLoadImage fullLoadImageOp = (Gen2.Alien.Higgs2.FullLoadImage)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddHiggs2FullLoadImage(ref m, timeout, fullLoadImageOp.AccessPassword, fullLoadImageOp.KillPassword, fullLoadImageOp.LockBits, fullLoadImageOp.PCWord, fullLoadImageOp.Epc, null);
            }
            else if (srp.Op is Gen2.Alien.Higgs3.FastLoadImage)
            {
                Gen2.Alien.Higgs3.FastLoadImage fastLoadImageOp = (Gen2.Alien.Higgs3.FastLoadImage)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddHiggs3FastLoadImage(ref m, timeout, fastLoadImageOp.CurrentAccessPassword, fastLoadImageOp.AccessPassword, fastLoadImageOp.KillPassword, fastLoadImageOp.PCWord, fastLoadImageOp.Epc, null);
            }
            else if (srp.Op is Gen2.Alien.Higgs3.LoadImage)
            {
                Gen2.Alien.Higgs3.LoadImage loadImageOp = (Gen2.Alien.Higgs3.LoadImage)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddHiggs3LoadImage(ref m, timeout, loadImageOp.CurrentAccessPassword, loadImageOp.AccessPassword, loadImageOp.KillPassword, loadImageOp.PCWord, loadImageOp.EpcAndUserData, null);
            }
            else if (srp.Op is Gen2.Alien.Higgs3.BlockReadLock)
            {
                Gen2.Alien.Higgs3.BlockReadLock blockReadLockOp = (Gen2.Alien.Higgs3.BlockReadLock)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddHiggs3BlockReadLock(ref m, timeout, blockReadLockOp.AccessPassword, blockReadLockOp.LockBits, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.ActivateSecureMode)//PA Command
            {
                Gen2.Denatran.IAV.ActivateSecureMode IAVDenatranOp = (Gen2.Denatran.IAV.ActivateSecureMode)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.AuthenticateOBU)//PA Command
            {
                Gen2.Denatran.IAV.AuthenticateOBU IAVDenatranOp = (Gen2.Denatran.IAV.AuthenticateOBU)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.OBUAuthID)//G0 Command
            {
                Gen2.Denatran.IAV.OBUAuthID IAVDenatranOp = (Gen2.Denatran.IAV.OBUAuthID)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.OBUAuthFullPass1)//G0 Command
            {
                Gen2.Denatran.IAV.OBUAuthFullPass1 IAVDenatranOp = (Gen2.Denatran.IAV.OBUAuthFullPass1)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.OBUAuthFullPass2)//G0 Command
            {
                Gen2.Denatran.IAV.OBUAuthFullPass2 IAVDenatranOp = (Gen2.Denatran.IAV.OBUAuthFullPass2)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.ActivateSiniavMode)//G0 Command
            {
                Gen2.Denatran.IAV.ActivateSiniavMode IAVDenatranOp = (Gen2.Denatran.IAV.ActivateSiniavMode)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.OBUReadFromMemMap)//G0 Command
            {
                Gen2.Denatran.IAV.OBUReadFromMemMap IAVDenatranOp = (Gen2.Denatran.IAV.OBUReadFromMemMap)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.OBUWriteToMemMap)//G0 Command
            {
                Gen2.Denatran.IAV.OBUWriteToMemMap IAVDenatranOp = (Gen2.Denatran.IAV.OBUWriteToMemMap)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.OBUAuthFullPass)//G0 Command
            {
                Gen2.Denatran.IAV.OBUAuthFullPass IAVDenatranOp = (Gen2.Denatran.IAV.OBUAuthFullPass)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.GetTokenId)//G0 Command
            {
                Gen2.Denatran.IAV.GetTokenId IAVDenatranOp = (Gen2.Denatran.IAV.GetTokenId)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.ReadSec)//IP63 Command
            {
                Gen2.Denatran.IAV.ReadSec IAVDenatranOp = (Gen2.Denatran.IAV.ReadSec)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.WriteSec)//IP63 Command
            {
                Gen2.Denatran.IAV.WriteSec IAVDenatranOp = (Gen2.Denatran.IAV.WriteSec)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.Denatran.IAV.G0PAOBUAuth)//G0-PA Command
            {
                Gen2.Denatran.IAV.G0PAOBUAuth IAVDenatranOp = (Gen2.Denatran.IAV.G0PAOBUAuth)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIAVDenatran(ref m, timeout, IAVDenatranOp, (uint)accessPassword, null);
            }
            else if (srp.Op is Gen2.IDS.SL900A)
            {
                Gen2.IDS.SL900A op = (Gen2.IDS.SL900A)srp.Op;
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)op.AccessPassword, srp);
                if (op is Gen2.IDS.SL900A.AccessFifo)
                {
                    embedLen = msgAddIdsSL900aAccessFifo(ref m, timeout, (Gen2.IDS.SL900A.AccessFifo)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.EndLog)
                {
                    embedLen = msgAddIdsSL900aEndLog(ref m, timeout, (Gen2.IDS.SL900A.EndLog)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.GetCalibrationData)
                {
                    embedLen = msgAddIdsSL900aGetCalibrationData(ref m, timeout, (Gen2.IDS.SL900A.GetCalibrationData)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.GetLogState)
                {
                    embedLen = msgAddIdsSL900aGetLogState(ref m, timeout, (Gen2.IDS.SL900A.GetLogState)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.GetSensorValue)
                {
                    embedLen = msgAddIdsSL900aGetSensorValue(ref m, timeout, (Gen2.IDS.SL900A.GetSensorValue)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.GetBatteryLevel)
                {
                    embedLen = msgAddIdsSL900aGetBatteryLevel(ref m, timeout, (Gen2.IDS.SL900A.GetBatteryLevel)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.Initialize)
                {
                    embedLen = msgAddIdsSL900aInitialize(ref m, timeout, (Gen2.IDS.SL900A.Initialize)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.SetCalibrationData)
                {
                    embedLen = msgAddIdsSL900aSetCalibrationData(ref m, timeout, (Gen2.IDS.SL900A.SetCalibrationData)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.SetLogMode)
                {
                    embedLen = msgAddIdsSL900aSetLogMode(ref m, timeout, (Gen2.IDS.SL900A.SetLogMode)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.SetSfeParameters)
                {
                    embedLen = msgAddIdsSL900aSetSfeParameters(ref m, timeout, (Gen2.IDS.SL900A.SetSfeParameters)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.StartLog)
                {
                    embedLen = msgAddIdsSL900aStartLog(ref m, timeout, (Gen2.IDS.SL900A.StartLog)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.GetMeasurementSetup)
                {
                    embedLen = msgAddIdsSL900aGetMeasurementSetupValue(ref m, timeout, (Gen2.IDS.SL900A.GetMeasurementSetup)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.SetPassword)
                {
                    embedLen = msgAddIdsSL900aSetPassword(ref m, timeout, (Gen2.IDS.SL900A.SetPassword)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.SetLogLimit)
                {
                    embedLen = msgAddIdsSL900aSetLogLimit(ref m, timeout, (Gen2.IDS.SL900A.SetLogLimit)op, null);
                }
                else if (srp.Op is Gen2.IDS.SL900A.SetShelfLife)
                {
                    embedLen = msgAddIdsSL900aSetShelfLife(ref m, timeout, (Gen2.IDS.SL900A.SetShelfLife)op, null);
                }
            }
            else if (srp.Op is Gen2.NxpGen2TagOp.SetReadProtect)
            {
                Gen2.NxpGen2TagOp.SetReadProtect setReadProtectOp = (Gen2.NxpGen2TagOp.SetReadProtect)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)setReadProtectOp.AccessPassword, srp);
                embedLen = msgAddNxpSetReadProtect(ref m, timeout, setReadProtectOp.AccessPassword, setReadProtectOp.ChipType, null);
            }
            else if (srp.Op is Gen2.NxpGen2TagOp.ResetReadProtect)
            {
                Gen2.NxpGen2TagOp.ResetReadProtect resetReadProtectOp = (Gen2.NxpGen2TagOp.ResetReadProtect)(srp.Op);
                if (resetReadProtectOp.ChipType.Equals(0x02))
                {
                    throw new FeatureNotSupportedException("NXP Reset Read protect command can be embedded only if the chip-type is G2il");
                }
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)resetReadProtectOp.AccessPassword, srp);
                embedLen = msgAddNxpResetReadProtect(ref m, timeout, resetReadProtectOp.AccessPassword, resetReadProtectOp.ChipType, null);
            }
            else if (srp.Op is Gen2.NxpGen2TagOp.ChangeEas)
            {
                Gen2.NxpGen2TagOp.ChangeEas changeEasOp = (Gen2.NxpGen2TagOp.ChangeEas)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)changeEasOp.AccessPassword, srp);
                embedLen = msgAddNxpChangeEas(ref m, timeout, changeEasOp.AccessPassword, changeEasOp.Reset, changeEasOp.ChipType, null);
            }
            else if (srp.Op is Gen2.NxpGen2TagOp.Calibrate)
            {
                Gen2.NxpGen2TagOp.Calibrate calibrateOp = (Gen2.NxpGen2TagOp.Calibrate)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)calibrateOp.AccessPassword, srp);
                embedLen = msgAddNxpCalibrate(ref m, timeout, calibrateOp.AccessPassword, null);
            }
            else if (srp.Op is Gen2.NXP.G2I.ChangeConfig)
            {
                Gen2.NXP.G2I.ChangeConfig configOp = (Gen2.NXP.G2I.ChangeConfig)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)configOp.AccessPassword, srp);
                embedLen = msgAddNxpChangeConfig(ref m, timeout, 0, configOp.ConfigWord, configOp.ChipType, null);
            }
            else if (srp.Op is Gen2.NXP.UCODE7.ChangeConfig)
            {
                Gen2.NXP.UCODE7.ChangeConfig configOp = (Gen2.NXP.UCODE7.ChangeConfig)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)configOp.AccessPassword, srp);
                embedLen = msgAddNxpUCODE7ChangeConfig(ref m, timeout, 0, configOp.ConfigWord, configOp.ChipType, null);
            }
            else if (srp.Op is Gen2.NXP.AES.Untraceable)
            {
                Gen2.NXP.AES.Untraceable untraceableOp = (Gen2.NXP.AES.Untraceable)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)accessPassword, srp);
                embedLen = msgAddGen2v2NxpUntraceable(ref m, timeout, 0, untraceableOp, null);
            }
            else if (srp.Op is Gen2.NXP.AES.Authenticate)
            {
                Gen2.NXP.AES.Authenticate authenticateOp = (Gen2.NXP.AES.Authenticate)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)accessPassword, srp);
                embedLen = msgAddGen2v2NxpAuthenticate(ref m, timeout, 0, authenticateOp, null);
            }
            else if (srp.Op is Gen2.NXP.AES.ReadBuffer)
            {
                Gen2.NXP.AES.ReadBuffer readBufferOp = (Gen2.NXP.AES.ReadBuffer)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)accessPassword, srp);
                embedLen = msgAddGen2v2NxpReadBuffer(ref m, timeout, 0, readBufferOp, null);
            }
            else if (srp.Op is Gen2.Impinj.Monza4.QTReadWrite)
            {
                Gen2.Impinj.Monza4.QTReadWrite readWriteOp = (Gen2.Impinj.Monza4.QTReadWrite)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, (int)readWriteOp.AccessPassword, srp);
                embedLen = msgAddMonza4QTReadWrite(ref m, timeout, 0, readWriteOp.ControlByte, readWriteOp.PayloadWord, null);
            }
            else if (srp.Op is Gen2.Impinj.Monza6.MarginRead)
            {
                Gen2.Impinj.Monza6.MarginRead marginReadOp = (Gen2.Impinj.Monza6.MarginRead)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddMonza6MarginRead(ref m, timeout, 0, marginReadOp.bank, marginReadOp.bitAddress, marginReadOp.maskBitLength, marginReadOp.mask, marginReadOp.ChipType, null);
            }
            else if (srp.Op is Gen2.Fudan)
            {
                Gen2.Fudan op = (Gen2.Fudan)srp.Op;

                if (srp.Op is Gen2.Fudan.Fudan_ReadReg)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanReadReg(ref m, timeout, (Gen2.Fudan.Fudan_ReadReg)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_WriteReg)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanWriteReg(ref m, timeout, (Gen2.Fudan.Fudan_WriteReg)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_LoadReg)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanLoadReg(ref m, timeout, (Gen2.Fudan.Fudan_LoadReg)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_ReadMem)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanReadMem(ref m, timeout, (Gen2.Fudan.Fudan_ReadMem)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_WriteMem)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanWriteMem(ref m, timeout, (Gen2.Fudan.Fudan_WriteMem)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_StateCheck)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanStateCheck(ref m, timeout, (Gen2.Fudan.Fudan_StateCheck)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_Auth)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanAuth(ref m, timeout, (Gen2.Fudan.Fudan_Auth)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_StartStopLog)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanStartStopLog(ref m, timeout, (Gen2.Fudan.Fudan_StartStopLog)op, null);
                }
                else if (srp.Op is Gen2.Fudan.Fudan_Measure)
                {
                    tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                    embedLen = msgAddFudanMeasure1(ref m, timeout, (Gen2.Fudan.Fudan_Measure)op, null);
                    //msgAddFudanMeasure2(ref m, timeout, (Gen2.Fudan.Fudan_Measure)op, null);
                }
            }
            else if (srp.Op is Gen2.Ilian.GEN2_ILN_TagSelectCommand)
            {
                Gen2.Ilian.GEN2_ILN_TagSelectCommand opILN = (Gen2.Ilian.GEN2_ILN_TagSelectCommand)srp.Op;
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddIlian(ref m, timeout, (Gen2.Ilian.GEN2_ILN_TagSelectCommand)opILN, null);
            }
            else if (srp.Op is Gen2.EMMicro.EM4325.GetSensorData)
            {
                Gen2.EMMicro.EM4325.GetSensorData opSensorData = (Gen2.EMMicro.EM4325.GetSensorData)srp.Op;
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddEM4325GetSensorData(ref m, timeout, 0, opSensorData, null);
            }
            else if (srp.Op is Gen2.EMMicro.EM4325.ResetAlarms)
            {
                Gen2.EMMicro.EM4325.ResetAlarms opResetAlarms = (Gen2.EMMicro.EM4325.ResetAlarms)srp.Op;
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, accessPassword, srp);
                embedLen = msgAddEM4325ResetAlarms(ref m, timeout, 0, opResetAlarms, null);
            }
            else if (srp.Op is ReadMemory)
            {
                ReadMemory readOp = (ReadMemory)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol,metadataFlags, 0, srp);
                embedLen = msgAddMemoryRead(ref m, timeout, readOp.memType, readOp.address, readOp.length);
            }
            else if (srp.Op is WriteMemory)
            {
                WriteMemory writeOp = (WriteMemory)(srp.Op);
                tm = prepEmbReadTagMultiple(ref m, timeout, antennas, filt, protocol, metadataFlags, 0, srp);
                embedLen = msgAddMemoryWrite(ref m, timeout, writeOp.memType, writeOp.address, writeOp.data);
            }
            if ((0 == tm) || (0 == embedLen))
            {
                throw new FAULT_INVALID_OPCODE_Exception();
            }
            m.Insert(tm, embedLen);

        }

        #region StartReading
        private Thread asyncReadThread = null;
        /// <summary>
        /// Start reading RFID tags in the background. The tags found will be
        /// passed to the registered read listeners, and any exceptions that
        /// occur during reading will be passed to the registered exception
        /// listeners. Reading will continue until stopReading() is called.
        /// </summary>
        public override void StartReading()
        {
            asyncStoppedEvent = new ManualResetEvent(false);
            tagOpSuccessCount = 0;
            tagOpFailuresCount = 0;
            ReadPlan rp = (ReadPlan)this.ParamGet("/reader/read/plan");

            if (rp is MultiReadPlan)
            {
                MultiReadPlan multiReadPlan = (MultiReadPlan)rp;
                isValidationSuccess = validateMultiReadPlan(multiReadPlan);
            }
            // True continuous read in M6e variants, if there's a consistent antenna list across the 
            // entire set of read plans.
            if ((M6eFamilyList.Contains(model) || model.Equals("M3e")) &&
                ((0 == (int)ParamGet("/reader/read/asyncOffTime")) || ((0 != (int)ParamGet("/reader/read/asyncOffTime")) && (IsDutyCycleSupported() || (model.Equals("M3e"))))) &&
                ((rp is SimpleReadPlan) || ((rp is MultiReadPlan) && isValidationSuccess)))
            {
                if (0 == (int)ParamGet("/reader/read/asyncOffTime"))
                {
                    isDutyCycleFlag = false;
                }
                else
                {
                    isDutyCycleFlag = true;
                }
                _exitNow = false;
                _runNow = true;
                enableStreaming = true;
                finishedReading = false;
                isTrueContinuousRead = true;
                userMeta = (TagMetadataFlag)ParamGet("/reader/metadata");
                if (null == asyncReadThread)
                {
                    asyncReadThread = new Thread(StartContinuousRead);
                    asyncReadThread.IsBackground = true;
                    asyncReadThread.Start();
                }
            }
            // Fall back to pseudo-async mode
            else
            {
                isDutyCycleFlag = false;
                StartReadingGivenRead();
            }
            isReadInitiated = true;
            waitUntilReadMethodCalled.WaitOne();
        }

        private bool IsDutyCycleSupported()
        {
            try
            {
                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M6E:
                    case MODEL_M6E_I:
                        checkVersion = new Version("1.33.1.7"); //Converted to Decimal Version. Actual Version is 1.21.1.7.
                        break;
                    case MODEL_MICRO:
                        checkVersion = new Version("1.9.0.2");
                        break;
                    case MODEL_M6E_NANO:
                        checkVersion = new Version("1.7.0.2");
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsMultipleSupported()
        {
            try
            {
                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M6E:
                    case MODEL_M6E_I:
                        checkVersion = new Version("1.35.0.21"); //Converted to Decimal Version. Actual Version is 1.23.0.15.
                        break;
                    case MODEL_MICRO:
                        checkVersion = new Version("1.11.1.44"); //Converted to Decimal Version. Actual Version is 1.B.1.2C.
                        break;
                    case MODEL_M6E_NANO:
                        checkVersion = new Version("1.9.0.13"); //Converted to Decimal Version. Actual Version is 1.9.0.D.
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsAntennaReadTimeEnabled()
        {
            try
            {
                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M6E:
                    case MODEL_M6E_I:
                        checkVersion = new Version("1.35.0.5"); //Converted to Decimal Version. Actual Version is 1.23.0.5.
                        break;
                    case MODEL_MICRO:
                        checkVersion = new Version("1.11.0.20"); //Converted to Decimal Version. Actual Version is 1.B.0.14.
                        break;
                    case MODEL_M6E_NANO:
                        checkVersion = new Version("1.9.0.5"); //Converted to Decimal Version. Actual Version is 1.9.0.5.
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Logical antenna extention(to 64 from 32) feature 
        /// </summary>
        /// <returns></returns>
        private bool IsExtendedLogicalAntennaSupported()
        {
            try
            {
                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M6E:
                    case MODEL_M6E_I:
                        checkVersion = new Version("1.35.1.7"); //Converted to Decimal Version. Actual Version is 1.23.1.7.
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// is custom region configuration supported
        /// </summary>
        /// <returns></returns>
        private bool IsRegionConfgurationSupported()
        {
            try
            {
                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M6E:
                    case MODEL_M6E_I:
                        checkVersion = new Version("1.23.1.10"); //Converted to Decimal Version. Actual Version is 1.23.1.A.
                        break;
                    case MODEL_MICRO:
                        checkVersion = new Version("1.11.3.15"); //Converted to Decimal Version. Actual Version is 1.B.3.F.
                        break;
                    case MODEL_M6E_NANO:
                        checkVersion = new Version("1.9.0.10"); //Converted to Decimal Version. Actual Version is 1.9.0.A.
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool isAddrByteExtEnabled()
        {
            try
            {
                Version readerVersion = null;
                Version checkVersion = null;
                switch (_version.Hardware.Part1)
                {
                    case MODEL_M3E:
                        checkVersion=new Version("1.1.2.22");//Converted to Decimal Version. Actual Version is 1.1.2.16.
                        break;
                    default:
                        return false;
                }
                string tempreaderVersion = _version.Firmware.ToString();
                string[] versionSplit = _version.Firmware.ToString().Split('.');
                for (int i = 0; i < versionSplit.Length; i++)
                {
                    versionSplit[i] = int.Parse(versionSplit[i], System.Globalization.NumberStyles.HexNumber).ToString();
                }
                readerVersion = new Version(string.Join(".", versionSplit));

                if (checkVersion != null && readerVersion != null)
                {
                    if (readerVersion.CompareTo(checkVersion) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// method to validate features based on the reader.
        /// </summary>        
        public void checkForSupportedFeatures()
        {
            featureflag.Clear();
            if (IsDutyCycleSupported())
            {
                featureflag.Add(ReaderFeaturesFlag.READER_FEATURES_FLAG_DUTY_CYCLE);
            }
            if (IsMultipleSupported())
            {
                featureflag.Add(ReaderFeaturesFlag.READER_FEATURES_FLAG_MULTI_SELECT);
            }
            if (IsAntennaReadTimeEnabled())
            {
                featureflag.Add(ReaderFeaturesFlag.READER_FEATURES_FLAG_ANTENNA_READ_TIME);
            }
            if (IsExtendedLogicalAntennaSupported())
            {
                featureflag.Add(ReaderFeaturesFlag.READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA);
            }
            if(IsRegionConfgurationSupported())
            {
                featureflag.Add(ReaderFeaturesFlag.READER_FEATURES_FLAG_CUSTOM_REGION);
            }
            if (isAddrByteExtEnabled())
            {
                featureflag.Add(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION);
            }
        }


        private void StartContinuousRead()
        {
            try
            {
                while (!_exitNow)
                {
                    try
                    {
                        int readTime = (int)ParamGet("/reader/read/asyncOnTime");
                        Read(readTime);
                    }
                    catch (Exception ex)
                    {
                        if (-1 != ex.Message.IndexOf("No valid antennas specified"))
                        {
                            //Do nothing
                        }
                        else if (-1 != ex.Message.IndexOf("Command out of index range"))
                        {
                            _exitNow = true;
                            waitUntilReadMethodCalled.Set();
                            throw ex;
                        }
                        else
                        {
                            _exitNow = true;
                        }
                        notifyExceptionListeners(new ReaderException(ex.Message));
                        waitUntilReadMethodCalled.Set();
                    }
                }
            }
            // Catch all exceptions.  We're in a background thread,
            // so exceptions will be lost if we don't pass them on.
            catch (Exception ex)
            {
                ReadExceptionPublisher rx = new ReadExceptionPublisher(this, new ReaderException(ex.Message));
                Thread trd = new Thread(rx.OnReadException);
                trd.Start();
            }
        }

        #endregion

        #region StopReading

        /// <summary>
        /// Stop reading RFID tags in the background.
        /// </summary>
        public override void StopReading()
        {
            hasContinuousReadStarted = false;
            cmdStopContinousRead(StrStopReading);
        }

        private void cmdStopContinousRead(string value)
        {
            isTrueContinuousRead = false;
            hasContinuousReadStarted = false;
            {
                if (value.Equals(StrStopReading))
                {
                    if (isReadInitiated)
                    {
                        if (!enableStreaming)
                        {
                            StopReadingGivenRead();
                        }
                        else
                        {
                            _exitNow = true;
                            _runNow = false;
                            if (isReadStarted)
                            {
                                if (!continuousReadActive)
                                {
                                    asyncStoppedEvent.WaitOne();
                                }
                                CmdStopReading();

                                if (asyncReadThread != null)
                                {
                                    asyncReadThread.Join(1000);
                                    asyncReadThread = null;
                                }
                                waitForStopResponseEvent.WaitOne();
                                waitForStopResponseEvent.Reset();
                            }
                            if (asyncReadThread != null)
                            {
                                asyncReadThread.Join();
                                asyncReadThread = null;
                            }
                            waitUntilReadMethodCalled.Reset();
                        }
                    }
                }
                else
                {
                    CmdStopReading();
                    receiveBufferedReads();
                }
            }
            {
                enableStreaming = false;
                isGen2AllMemoryBankEnabled = false;
                isSecureAccessEnabled = false;
                isReadInitiated = false;
                isReadStarted = false;
                enableMultipleSelect = false;
                isEmbeddedTagOp = false;
                fetchTagReads = false;
                OffTimeAdded = false;
                isSubOffTime = false;
                isDutyCycleFlag = false;
                subOffTimeout = 0;
            }
        }

        private void CmdStopReading()
        {
            List<byte> cmd = new List<byte>();
            opCode = 0x2f;
            cmd.Add(0x2f);
            cmd.Add(0x00);
            cmd.Add(0x00);
            cmd.Add(0x02);
            if (hasContinuousReadStarted)
                hasContinuousReadStarted = false;
            int commandTimeout = (int)ParamGet("/reader/commandTimeout");
            sendMessage(cmd, ref opCode, commandTimeout);
        }

	
        #endregion

        #region KillTag

        /// <summary>
        /// Kill a tag. The first tag seen is killed.
        /// </summary>
        /// <param name="target">the tag to kill, or null</param>
        /// <param name="password">the authentication needed to kill the tag</param>
        public override void KillTag(TagFilter target, TagAuthentication password)
        {
            //CheckRegion();

            if (null == password)
                throw new ArgumentException("KillTag requires TagAuthentication: null not allowed");

            else if (password is Gen2.Password)
            {
                PrepForTagop();

                UInt32 pwval = ((Gen2.Password)password).Value;
                UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");

                CmdKillTag(timeout, pwval, target);
            }
            else
                throw new ArgumentException("Unsupported TagAuthentication: " + password.GetType().ToString());
        }

        #endregion

        #region LockTag

        /// <summary>
        /// Perform a lock or unlock operation on a tag. The first tag seen
        /// is operated on - the singulation parameter may be used to control
        /// this. Note that a tag without an access password set may not
        /// accept a lock operation or remain locked.
        /// </summary>
        /// <param name="target">the tag to lock, or null</param>
        /// <param name="action">the locking action to take</param>
        public override void LockTag(TagFilter target, TagLockAction action)
        {
            PrepForTagop();

            TagProtocol protocol = (TagProtocol)ParamGet("/reader/tagop/protocol");

            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            if (action is Gen2.LockAction)
            {
                if (TagProtocol.GEN2 != protocol)
                    throw new ArgumentException(string.Format("Gen2.LockAction not compatible with protocol {0}", protocol.ToString()));

                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                UInt32 accessPassword = pwobj.Value;
                Gen2.LockAction g2action = (Gen2.LockAction)action;

                CmdGen2LockTag(timeout, g2action.Mask, g2action.Action, accessPassword, target);
            }
            else if (action is Iso180006b.LockAction)
            {
                if (TagProtocol.ISO180006B != protocol)
                    throw new ArgumentException(string.Format("Iso180006b.LockAction not compatible with protocol {0}", protocol.ToString()));
                Iso180006b.LockAction i18kaction = (Iso180006b.LockAction)action;
                CmdIso180006bLockTag(timeout, i18kaction.Address, target);
            }

            else
                throw new ArgumentException("LockTag does not support this type of TagLockAction: " + action.ToString());
        }

        #endregion

        #region ReadTagMemBytes

        /// <summary>
        /// Read data from the memory bank of a tag. 
        /// </summary>
        /// <param name="target">the tag to read from, or null</param>
        /// <param name="bank">the tag memory bank to read from</param>
        /// <param name="byteAddress">the byte address to start reading at</param>
        /// <param name="byteCount">the number of bytes to read</param>
        /// <returns>the bytes read</returns>
        public override byte[] ReadTagMemBytes(TagFilter target, int bank, int byteAddress, int byteCount)
        {
            //CheckRegion();
            PrepForTagop();

            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            TagProtocol protocol = (TagProtocol)ParamGet("/reader/tagop/protocol");
            if (TagProtocol.GEN2 == protocol)
            {
                uint wordAddress = (uint)(byteAddress / 2);
                byte wordCount = (byte)(ByteConv.WordsPerBytes(byteCount));
                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                UInt32 password = pwobj.Value;
                Gen2.Bank bankobj = (Gen2.Bank)bank;
                TagReadData readData = CmdGen2ReadTagData(timeout, (TagMetadataFlag)0x00, bankobj, wordAddress, wordCount, password, target);
                byte[] memBytes = readData.Data;

                if (memBytes.Length == byteCount)
                    return memBytes;
                else
                {
                    byte[] trimmedBytes = new byte[byteCount];
                    if (byteCount == 0)
                    {
                        return memBytes;
                    }
                    else
                    {
                        Array.Copy(memBytes, trimmedBytes, byteCount);
                        return trimmedBytes;
                    }
                }
            }
            else if (TagProtocol.ISO180006B == protocol)
            {
                List<byte> result = new List<byte>();

                while (byteCount > 0)
                {
                    byte readSize = 8;
                    if (readSize > byteCount)
                    {
                        readSize = (byte)byteCount;
                    }
                    TagReadData readData = CmdIso180006bReadTagData(timeout, (byte)byteAddress, (byte)readSize, target);
                    result.AddRange(readData.Data);
                    byteCount -= readSize;
                    byteAddress += readSize;
                }

                return result.ToArray();
            }
            else
            {
                throw new ArgumentException("Reading tag data not supported for protocol " + protocol);
            }
        }

        #endregion

        #region ReadTagMemWords

        /// <summary>
        /// Read data from the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to read from, or null</param>
        /// <param name="bank">the tag memory bank to read from</param>
        /// <param name="wordAddress">the word address to start reading from</param>
        /// <param name="wordCount">the number of words to read</param>
        /// <returns>the words read</returns>
        public override ushort[] ReadTagMemWords(TagFilter target, int bank, int wordAddress, int wordCount)
        {
            return ReadTagMemWordsGivenReadTagMemBytes(target, bank, wordAddress, wordCount);
        }

        #endregion

        #region WriteTag

        /// <summary>
        /// Write a new ID to a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="epc">the new tag ID to write</param>
        public override void WriteTag(TagFilter target, TagData epc)
        {
            PrepForTagop();

            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            if (null != target)
            {
                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                UInt32 accessPassword = pwobj.Value;

                CmdWriteTagEpc(timeout, epc.EpcBytes, false, accessPassword, target);
            }
            else
            {
                CmdWriteTagEpc(timeout, epc.EpcBytes, false);
            }
        }

        #endregion

        #region readAfterWriteTagEPC

        /// <summary>
        /// Write a new ID to a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="epc">the new tag ID to write</param>
        /// <param name="readBank">the Gen2 memory bank to read from</param>
        /// <param name="readAddress">the word address to start reading from</param>
        /// <param name="readLen">the number of words to read</param>
        private ushort[] readAfterWriteTagEPC(TagFilter target, TagData epc, int readBank, int readAddress, int readLen)
        {
            PrepForTagop();
            TagReadData tr = new TagReadData();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            if (null != target)
            {
                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                UInt32 accessPassword = pwobj.Value;

                tr = CmdReadAfterWriteTagEpc(timeout, epc.EpcBytes, false, accessPassword, target, readBank, (uint)readAddress, (byte)readLen);
            }
            else
            {
                tr = CmdReadAfterWriteTagEpc(timeout, epc.EpcBytes, false, readBank, (uint)readAddress, (byte)readLen);
            }
            byte[] memBytes = tr.Data;
            ushort[] memWords = ByteFormat.bytesToWords(memBytes);

            return memWords;
        }

        #endregion

        #region WriteTagMemBytes

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="bank">the tag memory bank to write to</param>
        /// <param name="address">the byte address to start writing to</param>
        /// <param name="data">the bytes to write</param>
        public override void WriteTagMemBytes(TagFilter target, int bank, int address, ICollection<byte> data)
        {
            //CheckRegion();
            PrepForTagop();

            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            TagProtocol protocol = (TagProtocol)ParamGet("/reader/tagop/protocol");
            if (TagProtocol.GEN2 == protocol)
            {
                if (0 != (address & 1))
                    throw new ArgumentException("Byte memory address must be multiple of 2 (16-bit word-aligned).");

                Gen2.Bank bankobj = (Gen2.Bank)bank;
                uint wordAddress = (uint)address / 2;
                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");

                UInt32 password = pwobj.Value;


                List<ushort> wordData = new List<ushort>();
                for (int i = 0; i < data.Count; )
                {
                    wordData.Add(ByteConv.ToU16(CollUtil.ToArray(data), ref i));
                }

                switch ((Gen2.WriteMode)ParamGet("/reader/gen2/writeMode"))
                {
                    case Gen2.WriteMode.WORD_ONLY:
                        {
                            CmdGen2WriteTagData(timeout, bankobj, wordAddress, CollUtil.ToArray(data), password, target);
                            break;
                        }
                    case Gen2.WriteMode.BLOCK_ONLY:
                        {
                            BlockWrite(target, bankobj, wordAddress, wordData);
                            break;
                        }
                    case Gen2.WriteMode.BLOCK_FALLBACK:
                        {
                            try
                            {
                                BlockWrite(target, bankobj, wordAddress, wordData);
                            }
                            catch (FAULT_PROTOCOL_WRITE_FAILED_Exception)
                            {
                                CmdGen2WriteTagData(timeout, bankobj, wordAddress, CollUtil.ToArray(data), password, target);
                            }
                            break;
                        }
                    default: break;
                }




            }
            else if (TagProtocol.ISO180006B == protocol
                     || TagProtocol.ISO180006B_UCODE == protocol)
            {
                if (address < 0 || address > 255)
                {
                    throw new ArgumentException("Invalid memory address for " + protocol + ": " + address);
                }
                byte[] byteData = CollUtil.ToArray(data);
                CmdIso180006bWriteTagData(timeout, (byte)address, byteData, target);
            }
            else
            {
                throw new ArgumentException("Writing tag data not supported for protocol " + protocol);
            }
        }

        #endregion

        #region readAfterWriteTagMemWords

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="writeBank">the tag memory bank to write to</param>
        /// <param name="writeAddress">the byte address to start writing to</param>
        /// <param name="data">the bytes to write</param>
        /// <param name="readBank">the tag memory bank to read from</param>
        /// <param name="readAddress">the byte address to read from</param>
        /// <param name="readLen">the number of words to read</param>
        private ushort[] readAfterWriteTagMemWords(TagFilter target, int writeBank, int writeAddress, ICollection<ushort> data,
            int readBank, int readAddress, int readLen)
        {
            PrepForTagop();
            TagReadData tr = new TagReadData();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            TagProtocol protocol = (TagProtocol)ParamGet("/reader/tagop/protocol");
            if (TagProtocol.GEN2 == protocol)
            {
                if (0 != (writeAddress & 1))
                    throw new ArgumentException("Byte memory address must be multiple of 2 (16-bit word-aligned).");

                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");

                UInt32 password = pwobj.Value;

                ushort[] dataArray = CollUtil.ToArray(data);
                byte[] dataBytes = ByteFormat.wordsToBytes(dataArray);

                // Zero length read for M5e variants is not supported
                if ((readLen == 0) && M5eFamilyList.Contains(model))
                {
                    throw new ReaderException(
                        "Operation not supported. M5e does not support zero-length read.");
                }

                switch ((Gen2.WriteMode)ParamGet("/reader/gen2/writeMode"))
                {
                    case Gen2.WriteMode.WORD_ONLY:
                        {
                            tr = CmdGen2ReadAfterWriteTagData(timeout, (Gen2.Bank)writeBank, (uint)(writeAddress / 2), CollUtil.ToArray(dataBytes),
                                password, target, (Gen2.Bank)readBank, (uint)(readAddress), (byte)readLen);
                            break;
                        }
                    default:
                        throw new FeatureNotSupportedException("Unimplemented feature");

                }
            }
            else
            {
                throw new ArgumentException("Writing tag data not supported for protocol " + protocol);
            }

            byte[] memBytes = tr.Data;
            ushort[] memWords = ByteFormat.bytesToWords(memBytes);

            return memWords;
        }

        #endregion

        #region WriteTagMemWords

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="bank">the tag memory bank to write to</param>
        /// <param name="address">the word address to start writing to</param>
        /// <param name="data">the words to write</param>
        public override void WriteTagMemWords(TagFilter target, int bank, int address, ICollection<ushort> data)
        {
            ushort[] dataArray = CollUtil.ToArray(data);
            byte[] dataBytes = ByteFormat.wordsToBytes(dataArray);
            WriteTagMemBytes(target, bank, address * 2, dataBytes);

        }

        #endregion

        #region BlockWrite
        /// <summary>
        /// BlockWrite
        /// </summary>
        /// <param name="bank">the Gen2 memory bank to write to</param>
        /// <param name="wordPtr">the word address to start writing to</param>
        /// <param name="data">the data to write</param>
        /// <param name="target">the tag to write to, or null</param>
        void BlockWrite(TagFilter target, Gen2.Bank bank, uint wordPtr, ICollection<ushort> data)
        {
            PrepForTagop();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            Gen2.Bank bankobj = bank;
            Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
            UInt32 password = pwobj.Value;
            CmdBlockWrite(timeout, bankobj, wordPtr, (byte)(data.Count), CollUtil.ToArray(data), password, target);

        }
        #endregion

        #region BlockPermaLock
        /// <summary>
        /// BlockPermalock
        /// </summary>
        /// <param name="target">the tag to lock, or null</param>
        /// <param name="readLock">read or lock?</param>
        /// <param name="bank">memory bank</param>
        /// <param name="blockPtr">the staring word address to lock</param>
        /// <param name="blockRange">number of 16 blocks</param>
        /// <param name="mask">mask</param>
        /// <returns>the return data</returns>
        byte[] BlockPermaLock(TagFilter target, byte readLock, Gen2.Bank bank, uint blockPtr, byte blockRange, ushort[] mask)
        {
            PrepForTagop();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            Gen2.Bank bankobj = bank;
            Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
            UInt32 password = pwobj.Value;
            return CmdBlockPermaLock(timeout, readLock, bankobj, blockPtr, blockRange, mask, password, target);

        }
        void BlockErase(TagFilter target, Gen2.Bank bank, UInt32 wordPtr, byte wordCount)
        {
            PrepForTagop();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            Gen2.Bank bankobj = bank;
            Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
            UInt32 password = pwobj.Value;
            CmdEraseBlockTagSpecific(bank, wordPtr, wordCount, target);
        }

        #endregion

        #region WriteMemory
        /// <summary>
        /// WriteMemory functionality
        /// </summary>
        /// <param name="memType">the type of memory operation</param>
        /// <param name="address">the block number to start write data into</param>
        /// <param name="data">the data to write</param>
        /// <param name="target">the tag to write to - basically a filter</param>
        void WriteMemory(MemoryType memType, UInt32 address, ICollection<byte> data, TagFilter target)
        {
            PrepForTagop();
            if (IsOdd(data.Count))
                throw new ArgumentException("Number of bytes (" + data.Count.ToString() + ") must be even for Write Data operation");
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            CmdWriteMemory(memType, timeout, address, CollUtil.ToArray(data), 0, target);
        }
        #endregion

        #region ReadMemory
        /// <summary>
        /// ReadMemory Tag operation functionality
        /// </summary>
        /// <param name="memType">the type of memory operation to perform</param>
        /// <param name="address">the address location to start read memory from</param>
        /// <param name="length">the number of memory units to read </param>
        /// <param name="target">the tag to write to</param>
        byte[] ReadMemory(MemoryType memType, UInt32 address, byte length, TagFilter target)
        {
            PrepForTagop();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            return CmdReadMemory(timeout, memType, address, length, 0, target);
        }
        #endregion

        #region Passthrough
        /// <summary>
        /// Passthrough tag operation
        /// </summary>
        /// <param name="timeout">Timeout in msec </param>
        /// <param name="configFlags">Configuration flags - RFU</param>
        /// <param name="buffer">Command buffer </param>
        byte[] PassThrough(UInt32 timeout, UInt32 configFlags, List<byte> buffer)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x27); // opcode for Passthrough
            cmd.Add(0x00); //sub option - RFU
            cmd.AddRange(ByteConv.EncodeU16((ushort)timeout));
            cmd.AddRange(ConvertToEBV(configFlags));
            if (cmd.Count + buffer.Count > 255)
            {
                throw new ArgumentException("Value too big");
            }
            cmd.AddRange(CollUtil.ToArray(buffer));
            return GetDataFromM5eResponse(SendTimeout(cmd, (int)timeout));
        }
        #endregion

        #region ExecuteTagOp
        /// <summary>
        /// execute a TagOp
        /// </summary>
        /// <param name="tagOP">Tag Operation</param>
        /// <param name="target">Tag filter</param>
        ///<returns>the return value of the tagOp method if available</returns>

        public override Object ExecuteTagOp(TagOp tagOP, TagFilter target)
        {
            PrepForTagop();
            if ((!model.Equals("M3e")) &&((isMultipleSelectSupported) && (!(target is TagData))))
            {
                enableMultipleSelect = true;
            }
            try
            {
                if (tagOP is Gen2.Kill)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    Gen2.Password auth = new Gen2.Password(((Gen2.Kill)tagOP).KillPassword);
                    KillTag(target, auth);
                    return null;
                }
                else if (tagOP is Gen2.Lock)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    Gen2.LockAction g2action = new Gen2.LockAction(((Gen2.Lock)tagOP).LockAction);
                    Gen2.Password oldPassword = (Gen2.Password)(ParamGet("/reader/gen2/accessPassword"));
                    if (((Gen2.Lock)tagOP).AccessPassword != 0)
                    {
                        ParamSet("/reader/gen2/accessPassword", new Gen2.Password(((Gen2.Lock)tagOP).AccessPassword));
                    }
                    try
                    {
                        LockTag(target, g2action);
                    }
                    finally
                    {
                        ParamSet("/reader/gen2/accessPassword", oldPassword);
                    }
                    return null;
                }
                else if (tagOP is Gen2.SecureReadData)
                {
                    throw new ReaderException("Operation not supported");
                }
                else if (tagOP is Gen2.ReadData)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    int wordAddress = (int)((Gen2.ReadData)tagOP).WordAddress;
                    int wordCount = ((Gen2.ReadData)tagOP).Len;
                    // Zero length read for M5e variants is not supported
                    if ((wordCount == 0) && M5eFamilyList.Contains(model))
                    {
                        throw new ReaderException(
                            "Operation not supported. M5e does not support zero-length read.");
                    }
                    return ReadTagMemWords(target,
                        (int)(((Gen2.ReadData)tagOP).Bank), wordAddress, wordCount);
                }
                else if (tagOP is Gen2.WriteData)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    WriteTagMemWords(target, (int)(((Gen2.WriteData)tagOP).Bank), (int)((Gen2.WriteData)tagOP).WordAddress, ((Gen2.WriteData)tagOP).Data);
                    return null;
                }
                else if (tagOP is TagOpList)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    TagOpList tagOpList = (TagOpList)tagOP;
                    TagReadData tr = new TagReadData();
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 password = pwobj.Value;

                    if (tagOpList.list.Count == 1)
                    {
                        TagOp op = tagOpList.list[0];
                        return ExecuteTagOp(op, target);
                    }
                    else if (tagOpList.list.Count == 2)
                    {
                        byte[] bytes = new byte[] { };
                        ushort[] words = new ushort[] { };
                        if ((tagOpList.list[0] is Gen2.WriteData) && (tagOpList.list[1] is Gen2.ReadData))
                        {
                            Gen2.WriteData wdata = (Gen2.WriteData)tagOpList.list[0];
                            Gen2.ReadData rdata = (Gen2.ReadData)tagOpList.list[1];
                            return readAfterWriteTagMemWords(target, (int)(wdata.Bank), (int)(2 * (wdata.WordAddress)), wdata.Data, (int)(rdata.Bank), (int)(rdata.WordAddress), rdata.Len);
                        }
                        else if ((tagOpList.list[0] is Gen2.WriteTag) && (tagOpList.list[1] is Gen2.ReadData))
                        {
                            Gen2.WriteTag wtag = (Gen2.WriteTag)tagOpList.list[0];
                            Gen2.ReadData rdata = (Gen2.ReadData)tagOpList.list[1];
                            return readAfterWriteTagEPC(target, wtag.Epc, (int)(rdata.Bank), (int)(rdata.WordAddress), rdata.Len);
                        }
                        else
                        {
                            throw new FeatureNotSupportedException("Operation not supported");
                        }
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("Operation not supported");
                    }
                }
                else if (tagOP is Gen2.WriteTag)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    WriteTag(target, ((Gen2.WriteTag)tagOP).Epc);
                    return null;
                }
                else if (tagOP is Gen2.BlockWrite)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    BlockWrite(target, ((Gen2.BlockWrite)tagOP).Bank, ((Gen2.BlockWrite)tagOP).WordPtr, ((Gen2.BlockWrite)tagOP).Data);
                    return null;
                }
                else if (tagOP is Gen2.BlockPermaLock)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    return BlockPermaLock(target, ((Gen2.BlockPermaLock)tagOP).ReadLock, ((Gen2.BlockPermaLock)tagOP).Bank, ((Gen2.BlockPermaLock)tagOP).BlockPtr, ((Gen2.BlockPermaLock)tagOP).BlockRange, ((Gen2.BlockPermaLock)tagOP).Mask);
                }
                else if (tagOP is Gen2.BlockErase)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    BlockErase(target, ((Gen2.BlockErase)tagOP).Bank, ((Gen2.BlockErase)tagOP).WordPtr, ((Gen2.BlockErase)tagOP).WordCount);
                    return null;
                }
                else if (tagOP is Gen2.Alien.Higgs2.PartialLoadImage)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Alien.Higgs2.PartialLoadImage plImageOp = (Gen2.Alien.Higgs2.PartialLoadImage)tagOP;
                    CmdHiggs2PartialLoadImage(timeout, plImageOp.AccessPassword, plImageOp.KillPassword, plImageOp.Epc, target);
                    return null;
                }
                else if (tagOP is Gen2.Alien.Higgs2.FullLoadImage)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Alien.Higgs2.FullLoadImage flImageOp = (Gen2.Alien.Higgs2.FullLoadImage)tagOP;
                    CmdHiggs2FullLoadImage(timeout, flImageOp.AccessPassword, flImageOp.KillPassword, flImageOp.LockBits, flImageOp.PCWord, flImageOp.Epc, target);
                    return null;
                }
                else if (tagOP is Gen2.Alien.Higgs3.FastLoadImage)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Alien.Higgs3.FastLoadImage flImageOp = (Gen2.Alien.Higgs3.FastLoadImage)tagOP;
                    CmdHiggs3FastLoadImage(timeout, flImageOp.CurrentAccessPassword, flImageOp.AccessPassword, flImageOp.KillPassword, flImageOp.PCWord, flImageOp.Epc, target);
                    return null;
                }
                else if (tagOP is Gen2.Alien.Higgs3.LoadImage)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Alien.Higgs3.LoadImage lImageOp = (Gen2.Alien.Higgs3.LoadImage)tagOP;
                    CmdHiggs3LoadImage(timeout, lImageOp.CurrentAccessPassword, lImageOp.AccessPassword, lImageOp.KillPassword, lImageOp.PCWord, lImageOp.EpcAndUserData, target);
                    return null;
                }
                else if (tagOP is Gen2.Alien.Higgs3.BlockReadLock)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Alien.Higgs3.BlockReadLock bRLock = (Gen2.Alien.Higgs3.BlockReadLock)tagOP;
                    CmdHiggs3BlockReadLock(timeout, bRLock.AccessPassword, bRLock.LockBits, target);
                    return null;
                }
                else if (tagOP is Gen2.IDS.SL900A)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");

                    if (tagOP is Gen2.IDS.SL900A.AccessFifo)
                    {
                        return CmdIdsSL900aAccessFifo(timeout, (Gen2.IDS.SL900A.AccessFifo)tagOP, target);
                    }
                    if (tagOP is Gen2.IDS.SL900A.EndLog)
                    {
                        CmdIdsSL900aEndLog(timeout, (Gen2.IDS.SL900A.EndLog)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.GetCalibrationData)
                    {
                        return CmdIdsSL900aGetCalibrationData(timeout, (Gen2.IDS.SL900A.GetCalibrationData)tagOP, target);
                    }
                    else if (tagOP is Gen2.IDS.SL900A.GetLogState)
                    {
                        return CmdIdsSL900aGetLogState(timeout, (Gen2.IDS.SL900A.GetLogState)tagOP, target);
                    }
                    else if (tagOP is Gen2.IDS.SL900A.GetSensorValue)
                    {
                        return CmdIdsSL900aGetSensorValue(timeout, (Gen2.IDS.SL900A.GetSensorValue)tagOP, target);
                    }
                    else if (tagOP is Gen2.IDS.SL900A.GetBatteryLevel)
                    {
                        return CmdIdsSL900aGetBatteryLevel(timeout, (Gen2.IDS.SL900A.GetBatteryLevel)tagOP, target);
                    }
                    else if (tagOP is Gen2.IDS.SL900A.Initialize)
                    {
                        CmdIdsSL900aInitialize(timeout, (Gen2.IDS.SL900A.Initialize)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.SetCalibrationData)
                    {
                        CmdIdsSL900aSetCalibrationData(timeout, (Gen2.IDS.SL900A.SetCalibrationData)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.SetLogMode)
                    {
                        CmdIdsSL900aSetLogMode(timeout, (Gen2.IDS.SL900A.SetLogMode)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.SetSfeParameters)
                    {
                        CmdIdsSL900aSetSfeParameters(timeout, (Gen2.IDS.SL900A.SetSfeParameters)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.StartLog)
                    {
                        CmdIdsSL900aStartLog(timeout, (Gen2.IDS.SL900A.StartLog)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.GetMeasurementSetup)
                    {
                        return CmdIdsSL900aGetMeasurementSetup(timeout, (Gen2.IDS.SL900A.GetMeasurementSetup)tagOP, target);
                    }
                    else if (tagOP is Gen2.IDS.SL900A.SetPassword)
                    {
                        cmdIdsSL900aSetPassword(timeout, (Gen2.IDS.SL900A.SetPassword)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.SetLogLimit)
                    {
                        cmdIdsSL900aSetLogLimit(timeout, (Gen2.IDS.SL900A.SetLogLimit)tagOP, target);
                        return null;
                    }
                    else if (tagOP is Gen2.IDS.SL900A.SetShelfLife)
                    {
                        cmdIdsSL900aSetShelfLife(timeout, (Gen2.IDS.SL900A.SetShelfLife)tagOP, target);
                        return null;
                    }
                }
                else if (tagOP is Gen2.Denatran.IAV.ActivateSecureMode)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.ActivateSecureMode tagOperation = (Gen2.Denatran.IAV.ActivateSecureMode)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.AuthenticateOBU)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.AuthenticateOBU tagOperation = (Gen2.Denatran.IAV.AuthenticateOBU)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.ActivateSiniavMode)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.ActivateSiniavMode tagOperation = (Gen2.Denatran.IAV.ActivateSiniavMode)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.OBUAuthFullPass1)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.OBUAuthFullPass1 tagOperation = (Gen2.Denatran.IAV.OBUAuthFullPass1)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.OBUAuthFullPass2)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.OBUAuthFullPass2 tagOperation = (Gen2.Denatran.IAV.OBUAuthFullPass2)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.OBUAuthID)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.OBUAuthID tagOperation = (Gen2.Denatran.IAV.OBUAuthID)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.OBUReadFromMemMap)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.OBUReadFromMemMap tagOperation = (Gen2.Denatran.IAV.OBUReadFromMemMap)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.OBUWriteToMemMap)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.OBUWriteToMemMap tagOperation = (Gen2.Denatran.IAV.OBUWriteToMemMap)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.OBUAuthFullPass)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.OBUAuthFullPass tagOperation = (Gen2.Denatran.IAV.OBUAuthFullPass)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.GetTokenId)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.GetTokenId tagOperation = (Gen2.Denatran.IAV.GetTokenId)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.ReadSec)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.ReadSec tagOperation = (Gen2.Denatran.IAV.ReadSec)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.WriteSec)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.WriteSec tagOperation = (Gen2.Denatran.IAV.WriteSec)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.Denatran.IAV.G0PAOBUAuth)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Denatran.IAV.G0PAOBUAuth tagOperation = (Gen2.Denatran.IAV.G0PAOBUAuth)tagOP;
                    return CmdIAVDenatranCustomTagOp(timeout, tagOperation, accessPassword, target);
                }
                else if (tagOP is Gen2.NxpGen2TagOp.SetReadProtect)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.NxpGen2TagOp.SetReadProtect setReadProtectOp = (Gen2.NxpGen2TagOp.SetReadProtect)tagOP;
                    CmdNxpSetReadProtect(timeout, setReadProtectOp.AccessPassword, setReadProtectOp.ChipType, target);
                    return null;
                }
                else if (tagOP is Gen2.NxpGen2TagOp.ResetReadProtect)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.NxpGen2TagOp.ResetReadProtect resetReadProtectOp = (Gen2.NxpGen2TagOp.ResetReadProtect)tagOP;
                    CmdNxpResetReadProtect(timeout, resetReadProtectOp.AccessPassword, resetReadProtectOp.ChipType, target);
                    return null;
                }
                else if (tagOP is Gen2.NxpGen2TagOp.ChangeEas)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.NxpGen2TagOp.ChangeEas changeEasOp = (Gen2.NxpGen2TagOp.ChangeEas)tagOP;
                    CmdNxpChangeEas(timeout, changeEasOp.AccessPassword, changeEasOp.Reset, changeEasOp.ChipType, target);
                    return null;
                }
                else if (tagOP is Gen2.NxpGen2TagOp.EasAlarm)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.NxpGen2TagOp.EasAlarm easAlaramOp = (Gen2.NxpGen2TagOp.EasAlarm)tagOP;
                    return CmdNxpEasAlarm(timeout, easAlaramOp.DivideRatio, easAlaramOp.TagEncoding, easAlaramOp.TrExt, easAlaramOp.ChipType, target);
                }
                else if (tagOP is Gen2.NxpGen2TagOp.Calibrate)
                {
                    Gen2.NxpGen2TagOp.Calibrate calibrateOp = (Gen2.NxpGen2TagOp.Calibrate)tagOP;
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return CmdNxpCalibrate(timeout, calibrateOp.AccessPassword, target);

                }
                else if (tagOP is Gen2.NXP.G2I.ChangeConfig)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.NXP.G2I.ChangeConfig configChangeOp = (Gen2.NXP.G2I.ChangeConfig)tagOP;
                    return cmdNxpChangeConfig(timeout, configChangeOp.AccessPassword, configChangeOp.ConfigWord, configChangeOp.ChipType, target);

                }
                else if (tagOP is Gen2.NXP.UCODE7.ChangeConfig)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.NXP.UCODE7.ChangeConfig configChangeOp = (Gen2.NXP.UCODE7.ChangeConfig)tagOP;
                    cmdNxpUCODE7ChangeConfig(timeout, configChangeOp.AccessPassword, configChangeOp.ConfigWord, configChangeOp.ChipType, target);
                    return null;
                }
                else if (tagOP is Gen2.NXP.AES.Untraceable)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.NXP.AES.Untraceable UntraceableOp = (Gen2.NXP.AES.Untraceable)tagOP;
                    cmdGen2V2NxpUntraceable(timeout, accessPassword, UntraceableOp, target);
                    return null;
                }
                else if (tagOP is Gen2.NXP.AES.Authenticate)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.NXP.AES.Authenticate AuthenticateOp = (Gen2.NXP.AES.Authenticate)tagOP;
                    return cmdGen2V2NxpAuthenticate(timeout, accessPassword, AuthenticateOp, target);
                }
                else if (tagOP is Gen2.NXP.AES.ReadBuffer)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.NXP.AES.ReadBuffer ReadBufferOp = (Gen2.NXP.AES.ReadBuffer)tagOP;
                    return cmdGen2V2NxpReadBuffer(timeout, accessPassword, ReadBufferOp, target);
                }
                else if (tagOP is Gen2.Impinj.Monza4.QTReadWrite)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Impinj.Monza4.QTReadWrite qtReadWriteOp = (Gen2.Impinj.Monza4.QTReadWrite)tagOP;
                    return CmdMonza4QTReadWrite(timeout, qtReadWriteOp.AccessPassword, qtReadWriteOp.ControlByte, qtReadWriteOp.PayloadWord, target);
                }
                else if (tagOP is Gen2.Impinj.Monza6.MarginRead)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    Gen2.Impinj.Monza6.MarginRead marginReadOp = (Gen2.Impinj.Monza6.MarginRead)tagOP;
                    cmdMonza6MarginRead(timeout, accessPassword, marginReadOp.bank, marginReadOp.bitAddress, marginReadOp.maskBitLength, marginReadOp.mask, marginReadOp.ChipType, target);
                    return null;
                }
                else if (tagOP is Iso180006b.ReadData)
                {
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    ParamSet("/reader/tagop/protocol", TagProtocol.ISO180006B);
                    List<byte> result = new List<byte>();
                    int byteCount = ((Iso180006b.ReadData)tagOP).length;
                    byte byteAddress = ((Iso180006b.ReadData)tagOP).byteAddress;
                    while (byteCount > 0)
                    {
                        byte readSize = 8;
                        if (readSize > byteCount)
                        {
                            readSize = (byte)byteCount;
                        }
                        TagReadData readData = CmdIso180006bReadTagData(timeout, byteAddress, (byte)readSize, target);
                        result.AddRange(readData.Data);
                        byteCount -= readSize;
                        byteAddress += readSize;
                    }
                    return result.ToArray();
                }
                else if (tagOP is Iso180006b.WriteData)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.ISO180006B);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    CmdIso180006bWriteTagData(timeout, ((Iso180006b.WriteData)tagOP).Address, (byte[])((Iso180006b.WriteData)tagOP).Data, target);
                    return null;
                }
                else if (tagOP is Iso180006b.LockTag)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.ISO180006B);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    CmdIso180006bLockTag(timeout, ((Iso180006b.LockTag)tagOP).Address, target);
                    return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_ReadReg)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanReadReg(timeout, (Gen2.Fudan.Fudan_ReadReg)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_WriteReg)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanWriteReg(timeout, (Gen2.Fudan.Fudan_WriteReg)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_LoadReg)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanLoadReg(timeout, (Gen2.Fudan.Fudan_LoadReg)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_ReadMem)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanReadMem(timeout, (Gen2.Fudan.Fudan_ReadMem)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_WriteMem)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanWriteMem(timeout, (Gen2.Fudan.Fudan_WriteMem)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_StateCheck)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanStateCheck(timeout, (Gen2.Fudan.Fudan_StateCheck)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_Auth)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanAuth(timeout, (Gen2.Fudan.Fudan_Auth)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_StartStopLog)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanStartStopLog(timeout, (Gen2.Fudan.Fudan_StartStopLog)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Fudan.Fudan_Measure)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    return Cmd_init_FudanMeasure(timeout, (Gen2.Fudan.Fudan_Measure)tagOP, target);
                    //return null;
                }
                else if (tagOP is Gen2.Ilian.GEN2_ILN_TagSelectCommand)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Cmd_init_Ilian(timeout, (Gen2.Ilian.GEN2_ILN_TagSelectCommand)tagOP, target);
                    return null;
                }
                else if (tagOP is Gen2.EMMicro.EM4325.GetSensorData)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    return CmdEM4325GetSensorData(timeout, accessPassword, (Gen2.EMMicro.EM4325.GetSensorData)tagOP, target);
                }
                else if (tagOP is Gen2.EMMicro.EM4325.ResetAlarms)
                {
                    ParamSet("/reader/tagop/protocol", TagProtocol.GEN2);
                    UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
                    Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                    UInt32 accessPassword = pwobj.Value;
                    CmdEM4325ResetAlarms(timeout, accessPassword, (Gen2.EMMicro.EM4325.ResetAlarms)tagOP, target);
                    return null;
                }
                else if (tagOP is WriteMemory)
                {
                    WriteMemory writeOp = (WriteMemory)tagOP;
                    WriteMemory(writeOp.memType, writeOp.address, writeOp.data, target);
                    return null;
                }
                else if (tagOP is ReadMemory)
                {
                    ReadMemory readOp = (ReadMemory)tagOP;
                    return ReadMemory(readOp.memType, readOp.address, readOp.length, target);
                }
                else if (tagOP is PassThrough)
                {
                    PassThrough passThroughOp = (PassThrough)tagOP;
                    return PassThrough(passThroughOp.timeout, passThroughOp.configFlags, passThroughOp.buffer);
                }
                throw new Exception("Unsupported tagop: " + tagOP);
            }
            finally
            {
                enableMultipleSelect = false;
                isMultiFilterEnabled = false;
            }
        }

        #endregion

        #region multiProtocolSearch
        /// <summary>
        /// lv3 command supporting multiple protocol search
        /// </summary>
        /// <param name="op">opcode</param>
        /// <param name="readPlanList">readplan list</param>
        /// <param name="metadataFlags">metadataflags</param>
        /// <param name="antennas">antenna selection</param>
        /// <param name="timeout">timeout</param>
        /// <returns>collected tags</returns>
        public TagReadData[] CmdMultiProtocolSearch(CmdOpcode op, List<SimpleReadPlan> readPlanList, TagMetadataFlag metadataFlags, AntennaSelection antennas, ushort timeout)
        {
            List<byte> cmd = new List<byte>();
            cmd.AddRange(msgSetupmultiProtocolSearch(op, readPlanList, metadataFlags, antennas, timeout));
            TagProtocol tagProtocol = TagProtocol.NONE;

            byte[] response;

            List<TagReadData> collectedTags = new List<TagReadData>();
            uint tagsFound;

            if (op == CmdOpcode.TAG_READ_SINGLE)
            {
                response = SendTimeout(cmd, timeout);
                tagsFound = ByteConv.ToU32(response, 9);
                int readIdx = 13; //the start of the tag responses
                for (int i = 0; i < tagsFound; i++)
                {
                    int subBegin = readIdx;
                    if (readIdx == response.Length - 2)  //reach the CRC
                    {
                        break;
                    }
                    int subResponseLen = response[readIdx + 1];

                    readIdx = readIdx + 4 + 3;  //point to the start of the metadata ignore option and metaflags for read tag single

                    TagReadData read = new TagReadData();
                    ParseTagMetadata(ref read, response, ref readIdx, metadataFlags, ref tagProtocol);

                    int crclen = 2;
                    int epclen = subResponseLen + 4 - (readIdx - subBegin) - crclen;
                    byte[] epc = SubArray(response, readIdx, epclen);
                    byte[] crc = SubArray(response, readIdx + epclen, crclen);
                    TagData tag = null;
                    switch (tagProtocol)
                    {
                        default:
                            tag = new TagData(epc, crc);
                            break;
                        case TagProtocol.GEN2:
                            tag = new Gen2.TagData(epc, crc);
                            break;
                        case TagProtocol.ISO180006B:
                        case TagProtocol.ISO180006B_UCODE:
                            tag = new Iso180006b.TagData(epc, crc);
                            break;
                    }
                    read._tagData = tag;
                    collectedTags.Add(read);
                    readIdx = readIdx + epclen + crclen;  //point to the next protocol tag response
                }
            }

            if (op == CmdOpcode.TAG_READ_MULTIPLE)
            {
                if (enableStreaming)
                {
                    if (!continuousReadActive)
                    {
                        //only send start command and process its single response
                        SendTimeout(cmd, timeout);
                        continuousReadActive = true;
                        hasContinuousReadStarted = true;
                        asyncStoppedEvent.Set();
                        isReadStarted = true;
                        waitUntilReadMethodCalled.Set();
                        ReceiveResponseStream();
                    }
                    // Receipt of streaming responses will be handled separately
                }
                else
                {
                    waitUntilReadMethodCalled.Set();
                    response = SendTimeout(cmd, timeout);
                    tagsFound = ByteConv.ToU32(response, 9);
                    collectedTags.AddRange((GetAllTagReads(DateTime.Now, (int)tagsFound, tagProtocol)));
                }
            }
            return collectedTags.ToArray();
        }


        private object exception(string p)
        {
            throw new Exception("The method or operation is not implemented.");
        }
        #endregion

        #region msgAddGEN2DataWrite
        /// <summary>
        /// Assemble the embedded command for DataWrite
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="bank">The memory bank to write</param>
        /// <param name="wordAddress">Write starting address</param>
        /// <param name="data">The data to write</param>
        /// <returns>the length of the assembled embedded command</returns>
        [Obsolete()]
        public byte msgAddGEN2DataWrite(ref List<byte> msg, UInt16 timeout, Gen2.Bank bank, UInt32 wordAddress, ushort[] data)
        {
            msg.Add(0x24);//Add write opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00);//embedded cmd option,must be 0
            msg.AddRange(ByteConv.EncodeU32(wordAddress));//Add word address
            msg.Add((byte)bank);//Add bank
            msg.AddRange(ByteConv.ConvertFromUshortArray(data));//Add data
            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;
        }

        /// <summary>
        /// Assemble the command for DataWrite and readafterwrite functionality
        /// </summary>
        /// <param name="msg">The command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="writeMemBank">The memory bank to write</param>
        /// <param name="writeAddress">Write starting address</param>
        /// <param name="writeData">The data to write</param>
        /// <param name="accessPassword">The password to use when writing the tag</param>
        /// <param name="filter">A specification of the air protocol filtering to perform to find the tag</param>
        /// <param name="isReadAfterWrite">Supports read after write functionality</param>
        private void msgAddGEN2DataWrite(ref List<byte> msg, UInt16 timeout, Gen2.Bank writeMemBank, UInt32 writeAddress,
            byte[] writeData, UInt32 accessPassword, TagFilter filter, bool isReadAfterWrite)
        {
            msg.Add(0x24);
            msg.AddRange(ByteConv.EncodeU16(timeout));
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                if (!isReadAfterWrite)
                {
                    msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
                }
                else
                {
                    msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT | SINGULATION_OPTION_READ_AFTER_WRITE);
                }

            }
            // MakeSingulationBytes(TagFilter) takes care of all cases except Option 5. In WriteTagData we need
            // to send Option 5 for null singulation to allow sending access password. The hack is to send Option 4
            // with 0 select length and no Select Data. MakeSingulationBytes() with no params does exactly that.
            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (isReadAfterWrite && (!enableMultipleSelect))
            {
                msg.Add(SINGULATION_OPTION_READ_AFTER_WRITE); //option byte for read-after-write data
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add(sb.Option);
                msg.AddRange(ByteConv.EncodeU32(writeAddress));
                msg.Add((byte)writeMemBank);
                msg.AddRange(sb.Mask);
                msg.AddRange(writeData);
            }
            else
            {
                msg.Add((byte)(sbNewArray[0]));
                msg.AddRange(ByteConv.EncodeU32(writeAddress));
                msg.Add((byte)writeMemBank);
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                msg.AddRange(sbModified);
                msg.AddRange(writeData);
            }
        }

        #endregion

        #region msgAddGEN2DataRead
        /// <summary>
        /// Assemble the embedded command for DataRead
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="bank">The memory bank to read</param>
        /// <param name="wordAddress">Read starting address</param>
        /// <param name="length">The length of data to read</param>
        /// <param name="secureTagType"> Enum SecureTagType </param>
        /// <returns>the length of the assembled embedded command</returns>
        [Obsolete()]
        public byte msgAddGEN2DataRead(ref List<byte> msg, UInt16 timeout, Gen2.Bank bank, UInt32 wordAddress, byte length, Gen2.SecureTagType secureTagType)
        {
            msg.Add(0x28);//Add read opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(Convert.ToByte(secureTagType));//Embedded cmd option
            msg.Add((byte)bank);//Add bank
            msg.AddRange(ByteConv.EncodeU32(wordAddress));//Add word address
            msg.Add(length);//Add word count

            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;

        }
        #endregion

        #region msgAddGEN2LockTag
        /// <summary>
        /// Assemble the embedded command for Lock
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="accessPassword">The access password</param>
        /// <param name="mask">Bitmask indicating which lock bits to change </param>
        /// <param name="action">The lock action</param>
        /// <returns>the length of the assembled embedded command</returns>
        [Obsolete()]
        public byte msgAddGEN2LockTag(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, UInt16 mask, UInt16 action)
        {
            msg.Add(0x25);//Add lock opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00);//embedded cmd option,must be 0
            msg.AddRange(ByteConv.EncodeU32(accessPassword));//add accessPassword
            msg.AddRange(ByteConv.EncodeU16(mask));//add mask
            msg.AddRange(ByteConv.EncodeU16(action));//add mask

            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;

        }
        #endregion

        #region msgAddGEN2KillTag
        /// <summary>
        /// Assemble the embedded command for Kill
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="killPassword">Kill password to use to kill the tag</param>
        /// <returns>the length of the assembled embedded command</returns>
        [Obsolete()]
        public byte msgAddGEN2KillTag(ref List<byte> msg, UInt16 timeout, UInt32 killPassword)
        {
            msg.Add(0x26);//Add kill opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00);//embedded cmd option,must be 0
            msg.AddRange(ByteConv.EncodeU32(killPassword));//add Kill password
            msg.Add(0x00); // RFU

            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;
        }
        #endregion

        #region
        /// <summary>
        /// assemble the embedded command for BlockWrite
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">timeout</param>
        /// <param name="memBank">memory bank</param>
        /// <param name="wordPtr">word pointer</param>
        /// <param name="data">data</param>
        /// <param name="accessPassword">access password</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        [Obsolete()]
        public byte msgAddGEN2BlockWrite(ref List<byte> msg, UInt16 timeout, Gen2.Bank memBank, UInt32 wordPtr, ushort[] data, UInt32 accessPassword, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D); // opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00); // chiptype
            msg.Add((byte)(0x40 | sb.Option));
            msg.AddRange(new byte[] { 0x00, 0xC7 });//block write opcode
            msg.AddRange(sb.Mask);
            msg.Add((byte)0x00);//Write Flags
            msg.Add((byte)memBank);
            msg.AddRange(ByteConv.EncodeU32(wordPtr));
            msg.Add((byte)(data.Length));
            foreach (ushort i in data)
            {
                msg.AddRange(ByteConv.EncodeU16(i));
            }
            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;
        }

        #endregion

        #region msgAddGEN2BlockPermaLock
        /// <summary>
        /// BlockPermaLock
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">timeout</param>
        /// <param name="readLock">read or lock?</param>
        /// <param name="memBank">the tag memory bank to lock</param>
        /// <param name="blockPtr">the staring word address to lock</param>
        /// <param name="blockRange">number of 16 blocks</param>
        /// <param name="mask">mask</param>
        /// <param name="accessPassword">access password</param>
        /// <param name="target">the tag to lock</param>
        /// <returns>the length of the assembled embedded command</returns>
        [Obsolete()]
        public byte msgAddGEN2BlockPermaLock(ref List<byte> msg, UInt16 timeout, byte readLock, Gen2.Bank memBank, UInt32 blockPtr, byte blockRange, ushort[] mask, UInt32 accessPassword, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add((byte)0x2E);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add((byte)0x00);//chip type
            msg.Add((byte)(0x40 | sb.Option));//option
            msg.Add((byte)0x01);
            msg.AddRange(sb.Mask);
            msg.Add((byte)0x00);//RFU
            msg.Add(readLock);
            msg.Add((byte)memBank);
            msg.AddRange(ByteConv.EncodeU32(blockPtr));
            msg.Add(blockRange);
            if (readLock == 0x01)
            {
                foreach (ushort i in mask)
                {
                    msg.AddRange(ByteConv.EncodeU16(i));
                }

            }

            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;

        }

        #endregion

        #region
        /// <summary>
        /// WriteTagEpc
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">timeout</param>
        /// <param name="epc">New epc</param>
        /// <returns>the length of the assembled embedded command</returns>
        public byte msgAddGEN2WriteTagEPC(ref List<byte> msg, ushort timeout, TagData epc)
        {
            msg.Add(0x23);//Add read opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00);//RFU 2 bytes 
            msg.Add(0x00);
            msg.AddRange(epc.EpcBytes);

            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;
        }

        /// <summary>
        /// Assemble the command for writeTag and readafterwrite functionality
        /// </summary>
        /// <param name="msg">The command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="epc">New epc</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform to find the tag</param>
        /// <param name="isReadAfterWrite">Supports read after write functionality </param>
        private void msgAddGEN2WriteTagEPC(ref List<byte> msg, ushort timeout, byte[] epc, UInt32 accessPassword, TagFilter filter, bool isReadAfterWrite)
        {
            msg.Add(0x23);
            msg.AddRange(ByteConv.EncodeU16(timeout));
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                if (!isReadAfterWrite)
                {
                    msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
                }
                else
                {
                    msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT | SINGULATION_OPTION_READ_AFTER_WRITE);
                }

            }
            if (isReadAfterWrite && (!enableMultipleSelect))
            {
                msg.Add(SINGULATION_OPTION_READ_AFTER_WRITE); //option byte for read-after-write data
            }
            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (!isMultiFilterEnabled)
            {
                if (null != filter)
                {
                    msg.Add(sb.Option);
                    msg.AddRange(sb.Mask);
                }
                else
                {
                    if (!isReadAfterWrite)
                    {
                        msg.Add(00);
                    }
                    msg.Add(00);
                }
            }
            else
            {
                if (null != filter)
                {
                    msg.Add((byte)(sbNewArray[0]));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
                else
                {
                    if (!isReadAfterWrite)
                    {
                        msg.Add(00);
                    }
                    msg.Add(00);
                }
            }
            msg.AddRange(epc);
        }
        #endregion

        #region CmdGetTagsRemaining

        /// <summary>
        /// Get the number of tags stored in the tag buffer
        /// </summary>
        /// <returns>
        /// a three-element array containing: {the number of tags
        /// remaining, the current read index of the tag buffer, the
        /// current write index of the tag buffer}.
        /// </returns>
        [Obsolete()]
        public UInt16[] CmdGetTagsRemaining()
        {
            byte[] msg = { 0x29 };
            byte[] response = SendM5eCommand(msg);
            UInt16[] tagsRemaining = new UInt16[3];
            tagsRemaining[1] = ByteConv.ToU16(response, ARGS_RESPONSE_OFFSET + 0);
            tagsRemaining[2] = ByteConv.ToU16(response, ARGS_RESPONSE_OFFSET + 2);
            tagsRemaining[0] = (UInt16)(tagsRemaining[2] - tagsRemaining[1]);
            return tagsRemaining;
        }

        #endregion

        #region CmdClearTagBuffer

        /// <summary>
        /// Clear the tag buffer.
        /// </summary>
        [Obsolete()]
        public void CmdClearTagBuffer()
        {
            byte[] data = new byte[1];
            data[0] = 0x2A;
            byte[] inputBuffer = SendM5eCommand(data);
        }
        #endregion

        #region msgSetupReadTagSingle
        /// <summary>
        /// Assembly ReadTagSignle Cmd bytes
        /// </summary>
        /// <param name="metadataFlags">The metadata flags</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <param name="timeout">cmd timeout</param>
        /// <returns>ReadTagSignle Cmd bytes</returns>
        [Obsolete()]
        public List<byte> msgSetupReadTagSingle(TagMetadataFlag metadataFlags, TagFilter filter, ushort timeout)
        {
            List<byte> list = new List<byte>();
            list.Add(0x21);
            list.AddRange(ByteConv.EncodeU16(timeout)); //timeout
            SingulationBytes sb = MakeSingulationBytes(filter, false, 0);
            list.Add((byte)(sb.Option | (0x10)));  //with metadata
            list.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
            list.AddRange(sb.Mask);
            return list;
        }

        #endregion

        #region msgSetupReadTagMultiple
        /// <summary>
        /// Assembly ReadTagMultiple Cmd bytes
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <param name="antennas">the antenna or antennas to use for the search</param>
        /// <param name="filt">a specification of the air protocol filtering to perform</param>
        /// <param name="protocol">The reader's current tag protocol setting (controls formatting of the Read Tag Multiple command)</param>
        /// <param name="metadataFlags">The metadata flags</param>
        /// <param name="accessPassword">The access password</param>
        /// <returns>the command byte list</returns>
        [Obsolete()]
        public List<byte> msgSetupReadTagMultiple(UInt16 timeout, AntennaSelection antennas, TagFilter filt, TagProtocol protocol, TagMetadataFlag metadataFlags, int accessPassword)
        {
            isMultiFilterEnabled = false;
            if ((isMultipleSelectSupported) && (!(filt is TagData)))
            {
                enableMultipleSelect = true;
            }
            List<byte> list = new List<byte>();
            SingulationBytes singulation;
            if (model.Equals("M3e"))
            {
                singulation = MakeSingulationBytes(filt, false, 0);
            }
            else
            {
                singulation = MakeSingulationBytes(filt, true, (UInt32)accessPassword);
            }
            byte[] sbNewArray = new byte[singulation.Mask.Length];
            Array.Copy(singulation.Mask, 0, sbNewArray, 0, singulation.Mask.Length);
            if (isSecureAccessEnabled)
            {
                singulation.Option |= 0x40;
            }
            list.Add(0x22);
            if (enableMultipleSelect)
            {
                if (!enableReadAfterWrite)
                {
                    list.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
                }
                else
                {
                    list.Add(SINGULATION_OPTION_MULTIPLE_SELECT | SINGULATION_OPTION_READ_AFTER_WRITE);
                }
            }
            if (enableReadAfterWrite && (!enableMultipleSelect))
            {
                list.Add(SINGULATION_OPTION_READ_AFTER_WRITE); //option byte for read-after-write data
            }
            if (isMultiFilterEnabled)
            {
                if (isTriggerReadEnable)
                {
                    antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAG_GPI_TRIGGER_READ;
                }
                if (isDutyCycleFlag)
                {
                    antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_DUTY_CYCLE_CONTROL;
                }
                if (enableStreaming)
                {
                    list.Add((byte)((sbNewArray[0]) | SINGULATION_FLAG_METADATA_ENABLED));
                    antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_TAG_STREAMING | AntennaSelection.LARGE_TAG_POPULATION_SUPPORT;
                    if (0 != statusFlags)
                    {
                        if (statFlag == Stat.StatsFlag.NONE)
                        {
                            // Reader statistics
                            antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_STATUS_REPORT_STREAMING;
                        }
                        else
                        {
                            // Reader stats
                            antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_STATS_REPORT_STREAMING;
                        }
                    }
                    list.AddRange(ByteConv.EncodeU16((UInt16)(antennas)));
                }
                else
                {
                    list.Add((byte)((sbNewArray[0]) | SINGULATION_FLAG_METADATA_ENABLED));
                    antennas |= AntennaSelection.LARGE_TAG_POPULATION_SUPPORT;
                    list.AddRange(ByteConv.EncodeU16((UInt16)antennas));
                }
                list.AddRange(ByteConv.EncodeU16(timeout));
                if (isDutyCycleFlag)
                {
                    list.AddRange(ByteConv.EncodeU16((UInt16)((int)ParamGet("/reader/read/asyncOffTime"))));
                }
                list.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
                if (enableStreaming)
                {
                    if (0 != statusFlags)
                    {
                        list.AddRange(ByteConv.EncodeU16((UInt16)(statusFlags)));
                    }
                }
                if (isStopNTags)
                {
                    list.AddRange(ByteConv.EncodeU32(numberOfTagsToRead));
                }

                // We skip filterbytes() for a null filter and gen2 0 access
                // password so that we don't pass any filtering information at all
                // unless necessary; for some protocols (such as ISO180006B) the
                // "null" filter is not zero-length, but we don't need to send
                // that with this command.
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                list.AddRange(sbModified);
            }
            else
            {
                if (isStopNTags)
                {
                    antennas |= AntennaSelection.READ_MULTIPLE_RETURN_ON_N_TAGS;
                }
                if (isTriggerReadEnable)
                {
                    antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAG_GPI_TRIGGER_READ;
                }
                if (isDutyCycleFlag)
                {
                    antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_DUTY_CYCLE_CONTROL;
                }
                if (enableStreaming)
                {
                    list.Add((byte)((singulation.Option) | SINGULATION_FLAG_METADATA_ENABLED));
                    if (model.Equals("M3e"))
                    {
                        antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_TAG_STREAMING;
                    }
                    else
                    {
                        antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_TAG_STREAMING | AntennaSelection.LARGE_TAG_POPULATION_SUPPORT;
                    }
                    if (0 != statusFlags)
                    {
                        if (statFlag == Stat.StatsFlag.NONE)
                        {
                            // Reader statistics
                            antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_STATUS_REPORT_STREAMING;
                        }
                        else
                        {
                            // Reader stats
                            antennas |= AntennaSelection.READ_MULTIPLE_SEARCH_FLAGS_STATS_REPORT_STREAMING;
                        }
                    }
                    list.AddRange(ByteConv.EncodeU16((UInt16)(antennas)));
                }
                else
                {
                    list.Add((byte)(singulation.Option | SINGULATION_FLAG_METADATA_ENABLED));
                    if (!model.Equals("M3e"))
                    {
                        antennas |= AntennaSelection.LARGE_TAG_POPULATION_SUPPORT;
                    }

                    list.AddRange(ByteConv.EncodeU16((UInt16)antennas));
                }

                list.AddRange(ByteConv.EncodeU16(timeout));
                if (isDutyCycleFlag)
                {
                    if (isSubOffTime)
                    {
                        list.AddRange(ByteConv.EncodeU16((UInt16)subOffTimeout));
                    }
                    else
                    {
                        list.AddRange(ByteConv.EncodeU16((UInt16)((int)ParamGet("/reader/read/asyncOffTime"))));
                    }
                }
                if (isM6eVariant || model.Equals("M3e"))
                {
                    list.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
                }
                if (enableStreaming)
                {
                    if (0 != statusFlags)
                    {
                        list.AddRange(ByteConv.EncodeU16((UInt16)(statusFlags)));
                    }
                }

                if (isStopNTags)
                {
                    list.AddRange(ByteConv.EncodeU32(numberOfTagsToRead));
                }

                // We skip filterbytes() for a null filter and gen2 0 access
                // password so that we don't pass any filtering information at all
                // unless necessary; for some protocols (such as ISO180006B) the
                // "null" filter is not zero-length, but we don't need to send
                // that with this command.

                list.AddRange(singulation.Mask);
            }
            return list;

        }
        #endregion

        #region CmdReadTagMultiple

        /// <summary>
        /// Search for tags for a specified amount of time.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <param name="antennas">the antenna or antennas to use for the search</param>
        /// <param name="filt">a specification of the air protocol filtering to perform</param>
        /// <param name="protocol">The reader's current tag protocol setting (controls formatting of the Read Tag Multiple command)</param>
        /// <returns>the number of tags found</returns>
        [Obsolete()]
        public uint CmdReadTagMultiple(UInt16 timeout, AntennaSelection antennas, TagFilter filt, TagProtocol protocol)
        {
            isMultiFilterEnabled = false;
            List<byte> list = new List<byte>();
            list.Add(0x22);
            if (isMultipleSelectSupported && (!(filt is TagData)))
            {
                enableMultipleSelect = true;
            }

            Gen2.Password password = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
            UInt32 accessPassword = (UInt32)(password.Value);
            SingulationBytes singulation;
            if (model.Equals("M3e"))
            {
                singulation = MakeSingulationBytes(filt, false, 0);
            }
            else
            {
                singulation = MakeSingulationBytes(filt, true, accessPassword);
            }
            byte[] sbNewArray = new byte[singulation.Mask.Length];
            Array.Copy(singulation.Mask, 0, sbNewArray, 0, singulation.Mask.Length);
            // Add the option for bap parameters, if enabled
            //if both parameters are enabled they should be OR and 89 cmd has to be send
            //so added this validation 
            if (isBAPEnabled && enableMultipleSelect)
            {
                list.Add(0x89);
            }
            else
            {
                if (isBAPEnabled)
                {
                    list.Add(0x81);
                }
                if (enableMultipleSelect && (!isEmbeddedTagOp))
                {
                    list.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
                }
            }
            if (!isMultiFilterEnabled)
            {
                if (isM6eVariant || model.Equals("M3e"))
                {
                    list.Add((byte)((singulation.Option) | SINGULATION_FLAG_METADATA_ENABLED));
                }
                else
                {
                    list.Add((byte)(singulation.Option));
                }
                list.AddRange(ByteConv.EncodeU16((UInt16)antennas));
                list.AddRange(ByteConv.EncodeU16(timeout));
                if (isM6eVariant || (model.Equals("M3e")))
                {
                    list.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
                }
                if (isDutyCycleFlag)
                {
                    list.AddRange(ByteConv.EncodeU16((UInt16)((int)ParamGet("/reader/read/asyncOffTime"))));
                }
                if (isStopNTags)
                {
                    list.AddRange(ByteConv.EncodeU32(numberOfTagsToRead));
                }
                list.AddRange(singulation.Mask);
            }
            else
            {
                if (isM6eVariant || (model.Equals("M3e")))
                {
                    list.Add((byte)((sbNewArray[0]) | SINGULATION_FLAG_METADATA_ENABLED));
                }
                else
                {
                    list.Add((byte)(sbNewArray[0]));
                }
                list.AddRange(ByteConv.EncodeU16((UInt16)antennas));
                list.AddRange(ByteConv.EncodeU16(timeout));
                if (isDutyCycleFlag)
                {
                    list.AddRange(ByteConv.EncodeU16((UInt16)((int)ParamGet("/reader/read/asyncOffTime"))));
                }

                if (isStopNTags)
                {
                    list.AddRange(ByteConv.EncodeU32(numberOfTagsToRead));
                }
                if (isM6eVariant || model.Equals("M3e"))
                {
                    list.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
                }
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                list.AddRange(sbModified);
            }

            try
            {
                byte[] responsedata = null;
                waitUntilReadMethodCalled.Set();
                responsedata = GetDataFromM5eResponse(SendTimeout(list, timeout));
                if (null != responsedata)
                {
                    //4-byte count: Large-tag-population support and ISO18k
                    switch (responsedata.Length)
                    {
                            // For M3e, large tag population is not supported. Hence receives 1 byte tag count instead of 4 bytes from Firmware.
                        case 5:
                            return (uint)responsedata[4];
                        case 8:
                            return ByteConv.ToU32(responsedata, 4);
                        case 7:
                            return ByteConv.ToU32(responsedata, 3);
                        case 4:
                            return (uint)responsedata[3];
                        default:
                            throw new ReaderParseException("Unrecognized Read Tag Multiple response length: " + responsedata.Length);
                    }
                }
                else
                {
                    return default(uint);
                }
            }
            catch (ReaderCodeException ex)
            {
                if (ex is FAULT_NO_TAGS_FOUND_Exception)
                {
                    return 0;
                }
                else
                {
                    throw ex;
                }
            }
        }
        #endregion

        #region executeEmbeddedRead
        /// <summary>
        /// Execute the embedded command
        /// </summary>
        /// <param name="msg">The command bytes excluding SOH,Length and CRC</param>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <returns>The number of tag found</returns>
        public UInt32 executeEmbeddedRead(List<byte> msg, UInt16 timeout)
        {
            try
            {
                waitUntilReadMethodCalled.Set();
                byte[] responsedata = GetDataFromM5eResponse(SendTimeout(msg, timeout));
                int searchFlags = ByteConv.ToU16(responsedata, 1);
                if (enableMultipleSelect)
                {
                    searchFlags = ByteConv.ToU16(responsedata, 2);
                }
                if ((!enableMultipleSelect) && enableReadAfterWrite)
                {
                    searchFlags = ByteConv.ToU16(responsedata, 2);
                }
                if ((M6eFamilyList.Contains(model)) && ((searchFlags & (byte)AntennaSelection.LARGE_TAG_POPULATION_SUPPORT) != 0))
                {
                    if (enableMultipleSelect || enableReadAfterWrite)
                    {
                        tagOpSuccessCount = ByteConv.ToU16(responsedata, 10);
                        tagOpFailuresCount = ByteConv.ToU16(responsedata, 12);
                        return ByteConv.ToU32(responsedata, 4);
                    }
                    else
                    {
                        tagOpSuccessCount = ByteConv.ToU16(responsedata, 9);
                        tagOpFailuresCount = ByteConv.ToU16(responsedata, 11);
                        return ByteConv.ToU32(responsedata, 3);
                    }
                }
                else
                {
                    tagOpSuccessCount = ByteConv.ToU16(responsedata, 6);
                    tagOpFailuresCount = ByteConv.ToU16(responsedata, 8);
                    return Convert.ToUInt32(responsedata[3]);
                }
            }
            catch (FAULT_NO_TAGS_FOUND_Exception)
            {
                return 0;
            }
        }
        #endregion

        #region CmdReadTagSingle

        /// <summary>
        /// Search for a single tag for up to a specified amount of time.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag. Valid range is 0-65535</param>
        /// <param name="metadataFlags">the set of metadata values to retrieve and store in the returned object</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <param name="protocol">Tag protocol under which to interpret TagData content.</param>
        /// <returns>a TagReadData object containing the tag found and
        /// the metadata associated with the successful search, or
        /// null if no tag is found.</returns>
        [Obsolete()]
        public TagReadData CmdReadTagSingle(UInt16 timeout, TagMetadataFlag metadataFlags, TagFilter filter, TagProtocol protocol)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x21);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            SingulationBytes sb = MakeSingulationBytes(filter, false, 0);
            cmd.Add((byte)(sb.Option | (0x10)));  //with metadata
            cmd.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
            cmd.AddRange(sb.Mask);

            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            int ptr = 3;
            TagReadData read = new TagReadData();
            ParseTagMetadata(ref read, response, ref ptr, metadataFlags, ref protocol);

            int crclen = 2;
            int epclen = response.Length - ptr - crclen;
            byte[] epc = SubArray(response, ptr, epclen);
            byte[] crc = SubArray(response, ptr + epclen, crclen);
            TagData tag = null;
            switch (protocol)
            {
                default:
                    tag = new TagData(epc, crc);
                    break;
                case TagProtocol.GEN2:
                    tag = new Gen2.TagData(epc, crc);
                    break;
                case TagProtocol.ISO180006B:
                case TagProtocol.ISO180006B_UCODE:
                    tag = new Iso180006b.TagData(epc, crc);
                    break;
            }
            read._tagData = tag;
            return read;
        }

        #endregion

        #region GetAllTagReads

        /// <summary>
        /// Fetch all available tag reads from buffer and convert metadata to higher-level values
        /// </summary>
        /// <param name="baseTime">Time that search started.
        /// GetTagReads() only provides search-relative read times.
        /// This method adds an absolute reference time.</param>
        /// <param name="tagCount">Number of tag reads to fetch.</param>
        /// <param name="protocol">The reading protocol</param>
        /// <returns>List of tag read records</returns>
        private List<TagReadData> GetAllTagReads(DateTime baseTime, int tagCount, TagProtocol protocol)
        {
            List<TagReadData> tagReads = GetTagReads(tagCount, protocol);

            foreach (TagReadData read in tagReads)
            {
                read._baseTime = baseTime;
            }

            return tagReads;
        }
        #endregion

        #region GetTagReads

        /// <summary>
        /// Fetch tag reads from buffer using as many commands as necessary (not limited by capacity of a single command)
        /// </summary>
        /// <param name="tagCount">Number of tag reads to fetch from tag buffer</param>
        /// <param name="protocol">The reading protocol</param>
        /// <returns>List of tag read records</returns>
        public List<TagReadData> GetTagReads(int tagCount, TagProtocol protocol)
        {
            List<TagReadData> tagIDs = new List<TagReadData>();
            int tagsLeft = tagCount;


            timeStart = DateTime.Now;
            while (0 < tagsLeft)
            {
                TagReadData[] tags = CmdGetTagBuffer((TagMetadataFlag)ParamGet("/reader/metadata"), false, protocol);

                tagsLeft -= tags.Length;
                tagIDs.AddRange(RemoveCorruptedTags(tags));

            }

            return tagIDs;
        }

        #endregion

        #region RemoveCorruptedTags

        /// <summary>
        /// Ignoring invalid tag response (epcLen goes to negative)
        /// </summary>
        /// <param name="tags">list of read tags</param>
        /// <returns> Returns non corrupted tags in list </returns>
        private TagReadData[] RemoveCorruptedTags(TagReadData[] tags)
        {
            List<TagReadData> tagsUncorrupted = new List<TagReadData>();
            foreach (TagReadData tag in tags)
            {
                if (tag._tagData._crc != null || model.Equals("M3e"))
                {
                    tagsUncorrupted.Add(tag);
                }
            }
            return tagsUncorrupted.ToArray();
        }

        /// <summary>
        /// Ignoring invalid tag response (epcLen goes to negative)
        /// </summary>
        /// <param name="tags">list of read tags</param>
        /// <returns> Returns non corrupted tags in list </returns>
        private TagData[] RemoveCorruptedTags(TagData[] tags)
        {
            List<TagData> tagsUncorrupted = new List<TagData>();
            foreach (TagData tag in tags)
            {
                if (tag._crc != null)
                {
                    tagsUncorrupted.Add(tag);
                }
            }
            return tagsUncorrupted.ToArray();
        }

        #endregion

        #region CmdGetTagBuffer

        /// <summary>
        /// Get tag data of a number of tags from the tag buffer. This command moves a read index into the tag buffer, so that repeated calls will fetch all of the tags in the buffer. 
        /// </summary>
        /// <param name="count">the maximum of tags to get from the buffer. No more than 65535 may be requested. It is an error to request more tags than exist.</param>
        /// <param name="epc496">Whether the EPCs expected are 496 bits (true) or 96 bits (false)</param>
        /// <param name="protocol">Tag protocol under which to interpret TagData content.
        /// Will be overridden by fetched information if TagMetadataFlag.PROTOCOL is present in metadataFlags.</param>
        /// <returns>the tag data. Fewer tags may be returned than were requested.</returns>
        [Obsolete()]
        public TagData[] CmdGetTagBuffer(UInt16 count, bool epc496, TagProtocol protocol)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x29);
            cmd.AddRange(ByteConv.EncodeU16(count));

            byte[] rsp = SendM5eCommand(cmd);
            return RemoveCorruptedTags(ParseAllTagData(rsp, epc496, protocol));
        }

        /// <summary>
        /// Get tag data of a tags from certain locations in the tag buffer, without updating the read index. 
        /// </summary>
        /// <param name="start">the start index to read from</param>
        /// <param name="end">the end index to read to </param>
        /// <param name="epc496">Whether the EPCs expected are 496 bits (true) or 96 bits (false)</param>
        /// <param name="protocol">Tag protocol under which to interpret TagData content.
        /// Will be overridden by fetched information if TagMetadataFlag.PROTOCOL is present in metadataFlags.</param>
        /// <returns>the tag data. Fewer tags may be returned than were requested.</returns>
        [Obsolete()]
        public TagData[] CmdGetTagBuffer(UInt16 start, UInt16 end, bool epc496, TagProtocol protocol)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x29);
            cmd.AddRange(ByteConv.EncodeU16(start));
            cmd.AddRange(ByteConv.EncodeU16(end));

            byte[] rsp = SendM5eCommand(cmd);
            return RemoveCorruptedTags(ParseAllTagData(rsp, epc496, protocol));
        }

        /// <summary>
        /// Get tag data and associated read metadata from the tag buffer.
        /// </summary>
        /// <param name="metaDataFlags">the set of metadata values to retrieve and store in the returned objects</param>
        /// <param name="resend">whether to resend the same tag data sent in a previous call</param>
        /// <param name="protocol">Tag protocol under which to interpret TagData content.
        /// Will be overridden by fetched information if TagMetadataFlag.PROTOCOL is present in metadataFlags.</param>
        /// <returns>an array of TagReadData objects containing the tag and requested metadata</returns>
        [Obsolete()]
        public TagReadData[] CmdGetTagBuffer(TagMetadataFlag metaDataFlags, bool resend, TagProtocol protocol)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x29);
            cmd.AddRange(ByteConv.EncodeU16((UInt16)metaDataFlags));
            cmd.Add((byte)(resend ? 1 : 0));
            byte[] response = default(byte[]);
            List<TagReadData> tagReads = new List<TagReadData>();
            try
            {
                response = SendM5eCommand(cmd);
                int readOffset = ARGS_RESPONSE_OFFSET;
                // Update metadataFlags from response -- module may deny requested fields
                metadataFlags = (TagMetadataFlag)ByteConv.ToU16(response, ref readOffset);
                // Skip Read Options field
                readOffset++;

                int numTagsReceived = response[readOffset++];

                for (int itag = 0; itag < numTagsReceived; itag++)
                {
                    tagReads.Add(ParseTagReadData(response, ref readOffset, metadataFlags, protocol));
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return tagReads.ToArray();
        }

        #endregion

        #region CmdWriteTagEpc

        /// <summary>
        /// Write the EPC of a tag and update the PC bits. Behavior is
        /// unspecified if more than one tag can be found.
        /// </summary>
        /// <param name="timeout">
        /// the duration in milliseconds to search for a tag
        /// to write. Valid range is 0-65535
        /// </param>
        /// <param name="EPC">the EPC to write to the tag</param>
        /// <param name="Lock">whether to lock the tag (does not apply to all protocols)</param>
        [Obsolete()]
        public void CmdWriteTagEpc(UInt16 timeout, byte[] EPC, bool Lock)
        {
            CmdWriteTagEpc(timeout, EPC, Lock, ((Gen2.Password)ParamGet("/reader/gen2/accessPassword")).Value, null);
        }

        /// <summary>
        /// Write the EPC of a tag and update the PC bits. Behavior is
        /// unspecified if more than one tag can be found.
        /// </summary>
        /// <param name="timeout">
        /// the duration in milliseconds to search for a tag
        /// to write. Valid range is 0-65535
        /// </param>
        /// <param name="EPC">the EPC to write to the tag</param>
        /// <param name="Lock">whether to lock the tag (does not apply to all protocols)</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform to find the tag</param>
        [Obsolete()]
        public void CmdWriteTagEpc(UInt16 timeout, byte[] EPC, bool Lock, UInt32 accessPassword, TagFilter filter)
        {
            List<byte> list = new List<byte>();
            msgAddGEN2WriteTagEPC(ref list, timeout, EPC, accessPassword, filter, false);

            SendTimeout(list, timeout);


        }
        #endregion

        #region CmdReadAfterWriteTagEpc

        /// <summary>
        /// Write the EPC of a tag ,updates the PC bits and reads data from the requested memory bank. Behavior is
        /// unspecified if more than one tag can be found.
        /// </summary>
        /// <param name="timeout">
        /// the duration in milliseconds to search for a tag
        /// to write. Valid range is 0-65535
        /// </param>
        /// <param name="EPC">the EPC to write to the tag</param>
        /// <param name="Lock">whether to lock the tag (does not apply to all protocols)</param>
        /// <param name="readMemBank">the Gen2 memory bank to read from</param>
        /// <param name="readAddress">the word address to start reading from</param>
        /// <param name="count">the number of words to read</param>

        private TagReadData CmdReadAfterWriteTagEpc(UInt16 timeout, byte[] EPC, bool Lock, int readMemBank, UInt32 readAddress, byte count)
        {
            TagReadData t = new TagReadData();
            t = CmdReadAfterWriteTagEpc(timeout, EPC, Lock, ((Gen2.Password)ParamGet("/reader/gen2/accessPassword")).Value, null, readMemBank, readAddress, count);
            return t;
        }

        /// <summary>
        ///Write the EPC of a tag ,updates the PC bits and reads data from the requested memory bank. Behavior is
        /// unspecified if more than one tag can be found.
        /// </summary>
        /// <param name="timeout">
        /// the duration in milliseconds to search for a tag
        /// to write. Valid range is 0-65535
        /// </param>
        /// <param name="EPC">the EPC to write to the tag</param>
        /// <param name="Lock">whether to lock the tag (does not apply to all protocols)</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform to find the tag</param>
        /// <param name="readMemBank">the Gen2 memory bank to read from</param>
        /// <param name="readAddress">the word address to start reading from</param>
        /// <param name="count">the number of words to read</param>
        private TagReadData CmdReadAfterWriteTagEpc(UInt16 timeout, byte[] EPC, bool Lock, UInt32 accessPassword, TagFilter filter,
            int readMemBank, UInt32 readAddress, byte count)
        {
            List<byte> list = new List<byte>();
            msgAddGEN2WriteTagEPC(ref list, timeout, EPC, accessPassword, filter, true);
            list.Add((byte)readMemBank);
            list.AddRange(ByteConv.EncodeU32(readAddress));
            list.Add(count);
            byte[] response = GetDataFromM5eResponse(SendTimeout(list, timeout));
            int ptr = 2;
            TagReadData t = new TagReadData();
            t._tagData = new Gen2.TagData(new byte[0]);
            int datalength = response.Length - ptr;
            t._data = SubArray(response, ptr, datalength);
            return t;
        }

        #endregion


        #region CmdGen2WriteTagData

        /// <summary>
        /// Write data to a Gen2 tag.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag to write to. Valid range is 0-65535</param>
        /// <param name="memBank">the Gen2 memory bank to write to</param>
        /// <param name="address">the word address to start writing at</param>
        /// <param name="data">the data to write - must be an even number of bytes</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform to find the tag</param>
        [Obsolete()]
        public void CmdGen2WriteTagData(UInt16 timeout, Gen2.Bank memBank, UInt32 address, byte[] data, UInt32 accessPassword, TagFilter filter)
        {
            if (IsOdd(data.Length))
                throw new ArgumentException("Number of bytes (" + data.Length.ToString() + ") must be even for Gen2 Tag Write Data");

            List<byte> list = new List<byte>();
            msgAddGEN2DataWrite(ref list, timeout, memBank, address, data, accessPassword, filter, false);
            SendTimeout(list, timeout);
        }
        #endregion

        #region CmdGen2ReadAfterWriteTagData

        /// <summary>
        /// Write data to the requested memory bank and reads the 
        /// data from the requested memory bank from a Gen2 tag.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag to write to. Valid range is 0-65535</param>
        /// <param name="writeMemBank">the Gen2 memory bank to write to</param>
        /// <param name="writeAddress">the word address to start writing at</param>
        /// <param name="writeData">the data to write - must be an even number of bytes</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform to find the tag</param>
        /// <param name="readMemBank">the Gen2 memory bank to read from</param>
        /// <param name="readAddress">the word address to start reading from</param>
        /// <param name="count">the number of words to read</param>
        private TagReadData CmdGen2ReadAfterWriteTagData(UInt16 timeout, Gen2.Bank writeMemBank, UInt32 writeAddress, byte[] writeData, UInt32 accessPassword,
            TagFilter filter, Gen2.Bank readMemBank, UInt32 readAddress, byte count)
        {
            if (IsOdd(writeData.Length))
                throw new ArgumentException("Number of bytes (" + writeData.Length.ToString() + ") must be even for Gen2 Tag Write Data");

            List<byte> list = new List<byte>();
            msgAddGEN2DataWrite(ref list, timeout, writeMemBank, writeAddress, writeData, accessPassword, filter, true);
            list.Add((byte)readMemBank);
            list.AddRange(ByteConv.EncodeU32(readAddress));
            list.Add(count);
            byte[] response = GetDataFromM5eResponse(SendTimeout(list, timeout));
            int ptr = 2;
            TagReadData t = new TagReadData();
            t._tagData = new Gen2.TagData(new byte[0]);
            int datalength = response.Length - ptr;
            t._data = SubArray(response, ptr, datalength);
            return t;
        }
        #endregion

        #region CmdIso180006bWriteTagData

        /// <summary>
        /// Write data to an ISO180006B tag.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag to write to. Valid range is 0-65535</param>
        /// <param name="address">the byte address to start writing at</param>
        /// <param name="data">the data to write - must be an even number of bytes</param>
        /// <param name="filter">a specification of the air protocol filtering to perform to find the tag</param>
        [Obsolete()]
        public void CmdIso180006bWriteTagData(UInt16 timeout, byte address, byte[] data, TagFilter filter)
        {

            List<byte> list = new List<byte>();
            list.Add(0x24);
            list.AddRange(ByteConv.EncodeU16(timeout));

            if (filter != null
                && filter is TagData
                && ((TagData)filter).EpcBytes.Length == 8)
            {
                list.Add(0x08 | 0x02);    // Verify, don't use select data, write count provided
                list.Add(0x1B);    // Command is WRITE4BYTE
                list.Add(0);       // Don't lock
                list.Add(address);
                list.AddRange(((TagData)filter).EpcBytes);
            }
            else
            {
                list.Add(0x08 | 0x03);// Use select data, write count provided
                list.Add(0x1C);    // Command is WRITE4BYTE_MULTIPLE
                list.Add(0);       // Don't lock
                list.Add(address); // 8-bit address in this command form
                SingulationBytes sb = MakeIso180006bSingulationBytes(filter);
                // sb.Option is unused for ISO
                list.AddRange(sb.Mask);
            }
            list.AddRange(ByteConv.EncodeU16((ushort)data.Length));
            list.AddRange(data);
            SendTimeout(list, timeout);
        }
        #endregion

        #region CmdGen2LockTag
        /// <summary>
        /// Lock a Gen2 tag 
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag to lock. Valid range is 0-65535</param>
        /// <param name="mask">the Gen2 lock mask</param>
        /// <param name="action">the Gen2 lock action</param>
        /// <param name="accessPassword">the password to use when locking the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>       
        [Obsolete()]
        public void CmdGen2LockTag(UInt16 timeout, UInt16 mask, UInt16 action, UInt32 accessPassword, TagFilter filter)
        {
            List<byte> list = new List<byte>();
            list.Add(0x25);
            list.AddRange(ByteConv.EncodeU16(timeout));
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                list.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            SingulationBytes sb = MakeSingulationBytes(filter, false, accessPassword);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (!isMultiFilterEnabled)
            {
                list.Add(sb.Option);
                list.AddRange(ByteConv.EncodeU32(accessPassword));
                list.AddRange(ByteConv.EncodeU16(mask));
                list.AddRange(ByteConv.EncodeU16(action));
                list.AddRange(sb.Mask);
            }
            else
            {
                if (filter != null)
                {
                    list.Add((byte)sbNewArray[0]);
                }
                list.AddRange(ByteConv.EncodeU32(accessPassword));
                list.AddRange(ByteConv.EncodeU16(mask));
                list.AddRange(ByteConv.EncodeU16(action));
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                if (filter != null)
                {
                    list.AddRange(sbModified);
                }
            }

            SendTimeout(list, timeout);
        }
        #endregion

        #region CmdIso180006bLockTag
        /// <summary>
        /// Lock a byte of memory on an ISO180006B tag 
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag to lock. Valid range is 0-65535</param>
        /// <param name="address">Indicates the address of tag memory to be (un)locked.</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>       
        [Obsolete()]
        public void CmdIso180006bLockTag(UInt16 timeout, byte address, TagFilter filter)
        {
            if (filter == null
                || !(filter is TagData)
                || ((TagData)filter).EpcBytes.Length != 8)
            {
                throw new ArgumentException("ISO180006B only supports locking a single tag specified by 64-bit EPC");
            }

            List<byte> list = new List<byte>();
            list.Add(0x25);
            list.AddRange(ByteConv.EncodeU16(timeout));
            list.Add(0x01); // Command type and additional fields to follow
            list.Add(0x01); // Air command is QueryLock followed by Lock
            list.Add(address);
            list.AddRange(((TagData)filter).EpcBytes);
            SendTimeout(list, timeout);
        }
        #endregion

        #region CmdGen2ReadTagData

        /// <summary>
        /// Read the memory of a Gen2 tag.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for the operation. Valid range is 0-65535</param>
        /// <param name="metadataFlags">the set of metadata values to retrieve and store in the returned object</param>
        /// <param name="bank">the Gen2 memory bank to read from</param>
        /// <param name="address">the word address to start reading from</param>
        /// <param name="count">the number of words to read.</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <returns>
        /// a TagReadData object containing the tag data and any
        /// requested metadata (note: the tag EPC will not be present in the
        /// object)
        /// </returns>
        [Obsolete()]
        public TagReadData CmdGen2ReadTagData(UInt16 timeout, TagMetadataFlag metadataFlags, Gen2.Bank bank, UInt32 address, byte count, UInt32 accessPassword, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x28);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                cmd.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (!isMultiFilterEnabled)
            {
                cmd.Add((byte)(sb.Option | 0x10));
                cmd.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags));
                cmd.Add((byte)bank);
                cmd.AddRange(ByteConv.EncodeU32(address));
                cmd.Add(count);
                cmd.AddRange(sb.Mask);
            }
            else
            {
                cmd.Add((byte)(sbNewArray[0]));
                cmd.Add((byte)bank);
                cmd.AddRange(ByteConv.EncodeU32(address));
                cmd.Add(count);

                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                cmd.AddRange(sbModified);
            }

            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            int ptr = 3;
            if (enableMultipleSelect && isMultiFilterEnabled)
            {
                ptr = 2;
            }
            if (enableMultipleSelect && (!(isMultiFilterEnabled)))
            {
                ptr = 4;
            }
            TagReadData t = new TagReadData();
            TagProtocol protocol = TagProtocol.NONE;
            ParseTagMetadata(ref t, response, ref ptr, metadataFlags, ref protocol);
            switch (protocol)
            {
                default:
                    t._tagData = new TagData(new byte[0]);
                    break;
                case TagProtocol.GEN2:
                    t._tagData = new Gen2.TagData(new byte[0]);
                    break;
            }

            int datalength = response.Length - ptr;
            t._data = SubArray(response, ptr, datalength);
            return t;
        }
        #endregion

        #region CmdIso180006bReadTagData

        /// <summary>
        /// Read the memory of an ISO180006B tag.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for the operation. Valid range is 0-65535</param>
        /// <param name="address">the byte address to start reading from</param>
        /// <param name="count">the number of bytes to read.</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <returns>
        /// a TagReadData object containing the tag data and any
        /// requested metadata (note: the tag EPC will not be present in the
        /// object)
        /// </returns>
        [Obsolete()]
        public TagReadData CmdIso180006bReadTagData(UInt16 timeout, byte address, byte count, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();

            if (count > 8)
            {
                throw new ArgumentException("ISO180006B only supports reading 8 bytes at a time");
            }

            if (filter == null
                || !(filter is TagData)
                || ((TagData)filter).EpcBytes.Length != 8)
            {
                throw new ArgumentException("ISO180006B only supports reading from a single tag specified by 64-bit EPC");
            }

            cmd.Add(0x28);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x01); // Standard read operations
            cmd.Add(0x0C); // READ opcode
            cmd.Add(0);    // RFU
            cmd.Add(count);
            cmd.Add(address);
            cmd.AddRange(((TagData)filter).EpcBytes);

            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));

            TagReadData t = new TagReadData();
            t._tagData = new Iso180006b.TagData(new byte[0]);
            t._data = response;
            return t;
        }

        #endregion

        #region CmdKillTag

        /// <summary>
        /// Kill a Gen2 tag.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for a tag to kill. Valid range is 0-65535</param>
        /// <param name="password">Tag's kill password</param>
        /// <param name="filt">a specification of the air protocol filtering to perform</param>
        [Obsolete()]
        public void CmdKillTag(UInt16 timeout, UInt32 password, TagFilter filt)
        {
            List<byte> list = new List<byte>();
            list.Add(0x26);
            list.AddRange(ByteConv.EncodeU16(timeout));
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                list.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            SingulationBytes sing = MakeSingulationBytes(filt, false, 0);
            byte[] sbNewArray = new byte[sing.Mask.Length];
            Array.Copy(sing.Mask, 0, sbNewArray, 0, sing.Mask.Length);
            if (!isMultiFilterEnabled)
            {
                list.Add(sing.Option);
                list.AddRange(ByteConv.EncodeU32(password));
                list.Add(0x00); // RFU
                list.AddRange(sing.Mask);
            }
            else
            {
                if (filt != null)
                {
                    list.Add((byte)sbNewArray[0]);
                }

                list.AddRange(ByteConv.EncodeU32(password));
                list.Add(0x00); // RFU
                if (filt != null)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    list.AddRange(sbModified);
                }
            }

            SendTimeout(list, timeout);
        }

        #endregion

        #region CmdBlockWrite
        /// <summary>
        /// BlockWrite command
        /// </summary>
        /// <param name="timeout">timeout</param>
        /// <param name="memBank">the Gen2 memory bank to write to</param>
        /// <param name="wordPtr">the word address to start writing to</param>
        /// <param name="wordCount">the length of the data to write in words</param>
        /// <param name="data">the data to write</param>
        /// <param name="accessPassword">accessPassword</param>
        /// <param name="target">the tag to write to, or null</param>
        [Obsolete()]
        public void CmdBlockWrite(UInt16 timeout, Gen2.Bank memBank, UInt32 wordPtr, byte wordCount, ushort[] data, UInt32 accessPassword, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            cmd.Add((byte)0x2D); //opcode
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add((byte)0x00);//chip type
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                cmd.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                cmd.Add((byte)(0x40 | sb.Option));//option
                cmd.AddRange(new byte[] { 0x00, 0xC7 });//block write opcode
                cmd.AddRange(sb.Mask);
            }
            else
            {
                cmd.Add((byte)(0x40 | (byte)sbNewArray[0]));//option
                cmd.AddRange(new byte[] { 0x00, 0xC7 });//block write opcode
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                cmd.AddRange(sbModified);
            }
            cmd.Add((byte)0x00);//Write Flags
            cmd.Add((byte)memBank);
            cmd.AddRange(ByteConv.EncodeU32(wordPtr));
            cmd.Add(wordCount);
            foreach (ushort i in data)
            {
                cmd.AddRange(ByteConv.EncodeU16(i));
            }
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdBlockPermaLock
        /// <summary>
        /// BlockPermaLock
        /// </summary>
        /// <param name="timeout">timeout</param>
        /// <param name="readLock">read or lock?</param>
        /// <param name="memBank">the tag memory bank to lock</param>
        /// <param name="blockPtr">the staring word address to lock</param>
        /// <param name="blockRange">number of 16 blocks</param>
        /// <param name="mask">mask</param>
        /// <param name="accessPassword">access password</param>
        /// <param name="target">the tag to lock</param>
        /// <returns>the return data</returns>
        [Obsolete()]
        public byte[] CmdBlockPermaLock(UInt16 timeout, byte readLock, Gen2.Bank memBank, UInt32 blockPtr, byte blockRange, ushort[] mask, UInt32 accessPassword, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            cmd.Add((byte)0x2E);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add((byte)0x00);//chip type
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                cmd.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                cmd.Add((byte)(0x40 | sb.Option));//option
                cmd.Add((byte)0x01);
                cmd.AddRange(sb.Mask);
            }
            else
            {
                cmd.Add((byte)(0x40 | sbNewArray[0]));//option
                cmd.Add((byte)0x01);
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                cmd.AddRange(sbModified);
            }
            cmd.Add((byte)0x00);//RFU
            cmd.Add(readLock);
            cmd.Add((byte)memBank);
            cmd.AddRange(ByteConv.EncodeU32(blockPtr));
            cmd.Add(blockRange);
            if (readLock == 0x01)
            {
                foreach (UInt16 i in mask)
                {
                    cmd.AddRange(ByteConv.EncodeU16(i));
                }
            }

            byte[] response = SendTimeout(cmd, timeout);

            if (readLock == 0)
            {
                byte[] returnData = new byte[(response[1] - 2)];
                Array.Copy(response, 7, returnData, 0, (response[1] - 2));
                return returnData;
            }
            else
                return null;
        }

        #endregion

        #region CmdEraseBlockTagSpecific

        /// <summary>
        /// Erase a range of words on a Gen2 tag that supports the 
        /// optional Erase Block command.
        /// </summary>        
        /// <param name="bank">Memory bank</param>
        /// <param name="address">Address</param>
        /// <param name="count">Word Count</param>        
        /// <param name="target">target</param>
        private void CmdEraseBlockTagSpecific(Gen2.Bank bank, UInt32 address, byte count, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            UInt16 timeout = (UInt16)(int)ParamGet("/reader/commandTimeout");
            Gen2.Password accessPassword = (Gen2.Password)(ParamGet("/reader/gen2/accessPassword"));
            msgAddEraseBlockTagSpecific(ref cmd, timeout, bank, address, count, accessPassword.Value, target);
            SendTimeout(cmd, timeout);
        }

        /// <summary>
        /// Erase a range of words on a Gen2 tag that supports the 
        /// optional Erase Block command.
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">Timeout to erase block</param>
        /// <param name="bank">Memory bank</param>
        /// <param name="wordPtr">Address</param>
        /// <param name="wordCount">Word Count</param>
        /// <param name="accessPassword">the access password to erase the block on the tag</param>
        /// <param name="target">target</param>        
        private byte msgAddEraseBlockTagSpecific(ref List<byte> msg, UInt16 timeout, Gen2.Bank bank, UInt32 wordPtr, byte wordCount, UInt32 accessPassword, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2E);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x00);//chip type
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);//block erase
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (target != null)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }

                msg.Add(0x00);//block erase
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                msg.AddRange(sbModified);
            }

            msg.AddRange(ByteConv.EncodeU32(wordPtr));
            msg.Add((byte)bank);
            msg.Add(wordCount);
            return (byte)(msg.Count - tmp);
        }

        #endregion

        #region CmdReadTagAndKillMultiple
        /// <summary>
        /// Search for tags for a specified amount of time and kill each one.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <param name="antenna">the antenna or antennas to use for the search</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <param name="accessPassword">the access password to use when killing the tag</param>
        /// <param name="killPassword">the kill password to use when killing found tags</param>
        /// <returns>
        /// A three-element array: {the number of tags found, the
        /// number of tags successfully killed, the number of tags
        /// unsuccessfully killed}
        /// </returns>
        [Obsolete()]
        public int[] CmdReadTagAndKillMultiple(UInt16 timeout, AntennaSelection antenna, TagFilter filter, UInt32 accessPassword, UInt32 killPassword)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x22);
            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            cmd.Add((byte)(sb.Option));
            cmd.AddRange(ByteConv.EncodeU16((UInt16)((UInt16)antenna | (0x0004))));
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.AddRange(sb.Mask);
            cmd.Add(0x01);
            byte[] embeddedCommandAttr = PrepForEmbeddedKillTag(0, killPassword, MakeEmptySingulationBytes());
            cmd.Add((byte)(embeddedCommandAttr.Length - 1));
            cmd.AddRange(embeddedCommandAttr);
            return M5eToEmdCmdWithMultipleReadFormat(GetDataFromM5eResponse(SendTimeout(cmd, timeout)));
        }

        #endregion

        #region CmdReadTagAndLockMultiple

        /// <summary>
        /// Search for tags for a specified amount of time and lock each one.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <param name="antenna">the antenna or antennas to use for the search</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <param name="accessPassword">the password to use when locking the tag</param>
        /// <param name="mask">the Gen2 lock mask</param>
        /// <param name="action">the Gen2 lock action</param>
        /// <returns>
        /// A three-element array: {the number of tags found, the
        /// number of tags successfully locked, the number of tags
        /// unsuccessfully locked}
        /// </returns>
        [Obsolete()]
        public int[] CmdReadTagAndLockMultiple(UInt16 timeout, AntennaSelection antenna, TagFilter filter, UInt32 accessPassword, UInt16 mask, UInt16 action)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x22);
            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            cmd.Add((byte)(sb.Option));
            cmd.AddRange(ByteConv.EncodeU16((UInt16)((UInt16)antenna | (0x0004))));
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.AddRange(sb.Mask);
            cmd.Add(0x01);
            byte[] embeddedCommandAttr = PrepareForEmbeddedLockTag(0, accessPassword, mask, action, MakeEmptySingulationBytes());
            cmd.Add((byte)(embeddedCommandAttr.Length - 1));
            cmd.AddRange(embeddedCommandAttr);
            return M5eToEmdCmdWithMultipleReadFormat(GetDataFromM5eResponse(SendTimeout(cmd, timeout)));
        }
        #endregion

        #region CmdReadTagAndDataWriteMultiple

        /// <summary>
        /// Search for tags for a specified amount of time and write data to each one.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <param name="antenna">the antenna or antennas to use for the search</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="bank">the Gen2 memory bank to write to</param>
        /// <param name="address">the word address to start writing at</param>
        /// <param name="data">the data to write</param>
        /// <returns>
        /// A three-element array: {the number of tags found, the
        /// number of tags successfully written to, the number of tags
        /// unsuccessfully written to}.
        /// </returns>
        [Obsolete()]
        public int[] CmdReadTagAndDataWriteMultiple(UInt16 timeout, AntennaSelection antenna, TagFilter filter, UInt32 accessPassword, Gen2.Bank bank, UInt32 address, ICollection<byte> data)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x22);
            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            cmd.Add((byte)(sb.Option));
            cmd.AddRange(ByteConv.EncodeU16((UInt16)((UInt16)antenna | (0x0004))));
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.AddRange(sb.Mask);
            cmd.Add(0x01);
            byte[] embeddedCommandAttr = PrepareForEmbeddedWriteTagData(0, (byte)bank, address, CollUtil.ToArray(data), MakeEmptySingulationBytes());
            cmd.Add((byte)(embeddedCommandAttr.Length - 1));
            cmd.AddRange(embeddedCommandAttr);
            return M5eToEmdCmdWithMultipleReadFormat(GetDataFromM5eResponse(SendTimeout(cmd, timeout)));
        }

        #endregion

        #region CmdReadTagAndDataReadMultiple

        /// <summary>
        /// Search for tags for a specified amount of time and read data from each one.
        /// </summary>
        /// <param name="timeout">the duration in milliseconds to search for tags. Valid range is 0-65535</param>
        /// <param name="antenna">the antenna or antennas to use for the search</param>
        /// <param name="filter">a specification of the air protocol filtering to perform</param>
        /// <param name="accessPassword">the password to use when writing the tag</param>
        /// <param name="bank">the Gen2 memory bank to read from</param>
        /// <param name="address">the word address to start reading from</param>
        /// <param name="length">the number of words to read. Only two words per tag will be stored in the tag buffer.</param>
        /// <returns>
        /// A three-element array, containing: {the number of tags
        /// found, the number of tags successfully read from, the number
        /// of tags unsuccessfully read from}.
        /// </returns>
        [Obsolete()]
        public int[] CmdReadTagAndDataReadMultiple(UInt16 timeout, AntennaSelection antenna, TagFilter filter, UInt32 accessPassword, Gen2.Bank bank, UInt32 address, byte length)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x22);

            SingulationBytes sb = MakeSingulationBytes(filter, true, accessPassword);
            cmd.Add((byte)(sb.Option));
            cmd.AddRange(ByteConv.EncodeU16((UInt16)((UInt16)antenna | (0x0004))));
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.AddRange(sb.Mask);
            cmd.Add(0x01);

            byte[] embeddedCommandAttr = PrepareForEmbeddedReadTagData(0, (byte)bank, address, length, MakeEmptySingulationBytes());
            cmd.Add((byte)embeddedCommandAttr.Length);
            cmd.AddRange(embeddedCommandAttr);

            return M5eToEmdCmdWithMultipleReadFormat(GetDataFromM5eResponse(SendTimeout(cmd, timeout)));
        }
        #endregion

        #region CmdWriteMemory
        /// <summary>
        /// WriteData command
        /// </summary>
        /// <param name="memType">the type of memory operation</param>
        /// <param name="timeout">timeout</param>
        /// <param name="address">the address to start write memory to</param>
        /// <param name="data">the data to write</param>
        /// <param name="accessPassword">accessPassword</param>
        /// <param name="target">the tag to write to - filter</param>
        public void CmdWriteMemory(MemoryType memType, UInt16 timeout, UInt32 address, byte[] data, UInt32 accessPassword, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add((byte)0x24); //opcode
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (!isMultiFilterEnabled)
            {
                cmd.Add(sb.Option);
                cmd.Add((byte)memType);//type of write operation
                if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION))
                {
                    cmd.AddRange(ByteConv.EncodeU32(address));//Add word address    
                }
                else
                {
                    cmd.Add((byte)(address));
                }
                cmd.AddRange(sb.Mask);
            }
            else
            {
                cmd.Add(sbNewArray[0]);
                cmd.Add((byte)memType);//type of write operation
                if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION))
                {
                    cmd.AddRange(ByteConv.EncodeU32(address));//Add word address    
                }
                else
                {
                    cmd.Add((byte)(address));
                }
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                cmd.AddRange(sbModified);
            }
            if (cmd.Count + data.Length > 255)
            {
                throw new ArgumentException("Value too big");
            }
            cmd.AddRange(data);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region msgAddMemoryWrite
        /// <summary>
        /// Assemble the embedded command for MemoryWrite
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="memType">the type of memory operation to perform</param>
        /// <param name="address">the address location to start write data into</param>
        /// <param name="data">data to be written</param>
        /// <returns>the length of the assembled embedded command</returns>
        public byte msgAddMemoryWrite(ref List<byte> msg, UInt16 timeout, MemoryType memType, UInt32 address, byte[] data)
        {
            msg.Add(0x24);//Add read opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00);//Embedded cmd option
            msg.Add((byte)memType);
            if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION))
            {
                msg.AddRange(ByteConv.EncodeU32(address));//Add word address    
            }
            else
            {
                msg.Add((byte)(address));
            }
            msg.AddRange(data);
            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;
        }
        #endregion

        #region CmdReadMemory
        /// <summary>
        /// CmdReadMemory
        /// </summary>
        /// <param name="timeout">timeout</param>
        /// <param name="memType">the type of memory operation to perform</param>
        /// <param name="address">the address location to start read data from</param>
        /// <param name="length">number of memory units to be read</param>
        /// <param name="accessPassword">accessPassword</param>
        /// <param name="target">the tag to read data from - filter</param>
        public byte[] CmdReadMemory(UInt16 timeout, MemoryType memType, UInt32 address, byte length, UInt32 accessPassword, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddReadMemory(ref cmd, timeout, 0, memType, address, length, accessPassword, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            int readIndex = 3; // skip select option byte and 2 bytes of metadataflags.
            int datalength = response.Length - readIndex;
            return SubArray(response, readIndex, datalength); // return the data read
        }
        #endregion

        #region msgAddReadMemory
        /// <summary>
        /// msgAddReadMemory command
        /// </summary>
        /// <param name="cmd">the serial command to be sent to module</param>
        /// <param name="timeout">timeout</param>
        /// <param name="metadataFlags">metadata flags</param>
        /// <param name="memType">the type of memory operation to perform</param>
        /// <param name="address">the address location to start read data from</param>
        /// <param name="length">number of memory units to be read</param>
        /// <param name="accessPassword">accessPassword</param>
        /// <param name="target">the tag to read data from - filter</param>
        public void msgAddReadMemory(ref List<byte> cmd, UInt16 timeout, TagMetadataFlag metadataFlags, MemoryType memType, UInt32 address, byte length, UInt32 accessPassword, TagFilter target)
        {
            cmd.Add((byte)0x28); //opcode
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            SingulationBytes sb = MakeSingulationBytes(target, false, 0);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);

            if (!isMultiFilterEnabled)
            {
                cmd.Add((byte)(sb.Option | 0x10));
                cmd.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags)); //metadata flags
                cmd.Add((byte)memType);
                if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION))
                {
                    cmd.AddRange(ByteConv.EncodeU32(address));//Add word address    
                }
                else
                {
                    cmd.Add((byte)(address));
                }
                cmd.Add(length);
                cmd.AddRange(sb.Mask);
            }
            else
            {
                cmd.Add((byte)(sbNewArray[0] | 0x10));
                cmd.AddRange(ByteConv.EncodeU16((UInt16)metadataFlags)); //metadata flags
                cmd.Add((byte)memType);
                if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION))
                {
                    cmd.AddRange(ByteConv.EncodeU32(address));//Add word address    
                }
                else
                {
                    cmd.Add((byte)(address));
                }
                cmd.Add(length);
                byte[] sbModified = new byte[sbNewArray.Length - 1];
                Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                cmd.AddRange(sbModified);
            }
        }
        #endregion

        #region msgAddMemoryRead
        /// <summary>
        /// Assemble the embedded command for DataRead
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">The operation timeout</param>
        /// <param name="memType">the type of memory operation to perform</param>
        /// <param name="address">the address location to start read data from</param>
        /// <param name="length">number of elements to be read</param>
        /// <returns>the length of the assembled embedded command</returns>
        public byte msgAddMemoryRead(ref List<byte> msg, UInt16 timeout, MemoryType memType, UInt32 address, byte length)
        {
            msg.Add(0x28);//Add read opcode
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));//add time out
            msg.Add(0x00);//Embedded cmd option
            msg.Add((byte)memType);
            if (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION))
            {
                msg.AddRange(ByteConv.EncodeU32(address));//Add word address    
            }
            else
            {
                msg.Add((byte)(address));
            }
            msg.Add(length);//Add word count

            byte cmdLen = (byte)(msg.Count - tmp);
            return cmdLen;

        }
        #endregion

        #region ParseAllTagData

        /// <summary>
        /// Decode response of Get Tag Buffer (fixed-length formats only)
        /// </summary>
        /// <param name="rsp">Get Tag Buffer response (including SOF and CRC)</param>
        /// <param name="epc496">Record format.</param>
        /// <param name="protocol">Tag protocol under which to interpret TagData content.
        /// If true, records sized for 496-bit EPCs (66 bytes hold PC16,EPC496,CRC16).
        /// If false, records sized for 96-bit EPCs (16 bytes hold PC16,EPC96,CRC16).</param>
        /// <returns>Array of parsed TagData records</returns>
        private TagData[] ParseAllTagData(byte[] rsp, bool epc496, TagProtocol protocol)
        {
            List<TagData> tags = new List<TagData>();
            int readOffset = ARGS_RESPONSE_OFFSET;
            int reclen = epc496 ? 66 : 16;

            // Don't try to parse message CRC
            int datalen = rsp.Length - 2;

            while (readOffset < datalen)
            {
                TagData response = ParseTagData(rsp, ref readOffset, protocol, reclen);
                if (null != response)
                {
                    tags.Add(response);
                }
            }

            return tags.ToArray();
        }

        #endregion

        #region ParseTagReadData

        // TODO: Try to consolidate ParseTagReadData implementations into a single method with options
        // There are 3 commands that respond with TagReadData, with varying payloads:
        //  * ReadTagSingle: EPC,CRC -> Populate Tag field with appropriate subclass of TagData
        //  * GetTagBuffer: PC,EPC,CRC -> Populate Tag field with appropriate subclass of TagData
        //  * ReadTagData: Data -> Don't populate Tag field, fill Data with response payload instead
        /// <summary>
        /// Parse one tag read record out of an M5e GetTagBuffer (with metadata) response
        /// </summary>
        /// <param name="response">M5e binary response to GetTagBuffer command</param>
        /// <param name="readOffset">Offset of tag record to parse.  Will be incremented past end of record</param>
        /// <param name="metadataFlags">Metadata flags that were passed to GetTagBuffer to produce this record</param>
        /// <param name="protocol">Tag protocol under which to interpret TagData content.
        /// Will be overridden by fetched information if TagMetadataFlag.PROTOCOL is present in metadataFlags.</param>
        /// <returns>TagReadData representing binary record</returns>
        private TagReadData ParseTagReadData(byte[] response, ref int readOffset, TagMetadataFlag metadataFlags, TagProtocol protocol)
        {
            TagReadData t = new TagReadData();
            ParseTagMetadata(ref t, response, ref readOffset, metadataFlags, ref protocol);
            t._tagData = ParseTagData(response, ref readOffset, protocol, 0);
            return t;
        }

        #endregion

        #region ParseTagMetadata

        private void ParseTagMetadata(ref TagReadData t, byte[] response, ref int readOffset, TagMetadataFlag metadataFlags, ref TagProtocol protocol)
        {
            const TagMetadataFlag understoodFlags = 0
              | TagMetadataFlag.READCOUNT
              | TagMetadataFlag.RSSI
              | TagMetadataFlag.ANTENNAID
              | TagMetadataFlag.FREQUENCY
              | TagMetadataFlag.TIMESTAMP
              | TagMetadataFlag.PHASE
              | TagMetadataFlag.PROTOCOL
              | TagMetadataFlag.DATA
              | TagMetadataFlag.GPIO
              | TagMetadataFlag.GEN2_Q
              | TagMetadataFlag.GEN2_LF
              | TagMetadataFlag.GEN2_TARGET
              | TagMetadataFlag.BRAND_IDENTIFIER
              | TagMetadataFlag.TAGTYPE;

            if (metadataFlags != (metadataFlags & understoodFlags))
            {
                TagMetadataFlag misunderstoodFlags = metadataFlags & (~understoodFlags);
                throw new ReaderParseException("Unknown metadata flag bits: 0x" +
                                                   ((int)misunderstoodFlags).ToString("X4"));
            }
            t.metaDataFlags = metadataFlags;

            if (0 != (metadataFlags & TagMetadataFlag.READCOUNT))
            {
                t.ReadCount = response[readOffset++];
            }
            if (0 != (metadataFlags & TagMetadataFlag.RSSI))
            {
                t._lqi = (sbyte)(response[readOffset++]);
            }
            if (0 != (metadataFlags & TagMetadataFlag.ANTENNAID))
            {
                if (isTxRxMapSet)
                {
                    t._antenna = TranslateDefaultSerialAntenna(response[readOffset++]);
                }
                else
                {
                    if (!autonomousStreaming)
                    {
                        t._antenna = _txRxMap.TranslateSerialAntenna(response[readOffset++]);
                    }
                    else
                    {
                        t._antenna = TranslateSerialAntenna(response[readOffset++]);
                    }
                }
            }
            if (0 != (metadataFlags & TagMetadataFlag.FREQUENCY))
            {
                t._frequency = response[readOffset++] << 16;
                t._frequency |= response[readOffset++] << 8;
                t._frequency |= response[readOffset++] << 0;
            }
            if (0 != (metadataFlags & TagMetadataFlag.TIMESTAMP))
            {
                t._readOffset = response[readOffset++] << 24;
                t._readOffset |= response[readOffset++] << 16;
                t._readOffset |= response[readOffset++] << 8;
                t._readOffset |= response[readOffset++] << 0;
            }
            if (0 != (metadataFlags & TagMetadataFlag.PHASE))
            {
                t._phase = response[readOffset++] << 8;
                t._phase |= response[readOffset++] << 0;
            }
            if (0 != (metadataFlags & TagMetadataFlag.PROTOCOL))
            {
                // TagData.Protocol is not directly modifiable.
                // It is changed by subclassing TagData.
                // Just remember it here in order to to create the right class later.
                protocol = TranslateProtocol((SerialTagProtocol)response[readOffset++]);
            }

            if (0 != (metadataFlags & TagMetadataFlag.DATA))
            {
                if (enableStreaming)
                {
                    tagOpSuccessCount = 1;
                }
                int bitlength = ByteConv.ToU16(response, readOffset);
                int length;
                // If MSB of data length field is set, then it indicates tag operation is failed
                if ((bitlength & 0x8000) == 0x8000)
                {
                    length = 2; // always 2 bytes of error code
                    t.isErrorData = true; // the data here is error data not the actual data.
                    t._data = new byte[length];
                    // Extract 2 bytes of error code and store in t._data variable to show to user.
                    Array.Copy(response, (readOffset + 2), t._data, 0, length);
                }
                else
                {
                    length = ((bitlength + 7) / 8);
                    t.isErrorData = false; // the data here is actual data.
                    t._data = new byte[length];
                    if (isM6eVariant)
                    {
                        //In UHF data length is stored in bytes.
                        t.dataLength = length;
                    }
                    else
                    {
                        // In M3e, data length is stored in bits.
                        t.dataLength = bitlength;
                    }
                    Array.Copy(response, (readOffset + 2), t._data, 0, length);
                    if (isGen2AllMemoryBankEnabled)
                    {
                        ParseTagMemBankdata(ref t, t.Data, 0);
                    }
                }
                readOffset += (2 + length);
            }

            if (0 != (metadataFlags & TagMetadataFlag.GPIO))
            {
                byte gpioByte = response[readOffset++];
                if (!autonomousStreaming)
                {
                    switch (_version.Hardware.Part1)
                    {
                        case MODEL_M6E:
                            gpioNumber = 4;
                            break;
                        case MODEL_M5E:
                        case MODEL_MICRO:
                            gpioNumber = 2;
                            break;
                        case MODEL_M3E:
                            gpioNumber = 4;
                            break;
                        default:
                            gpioNumber = 4;
                            break;
                    }
                }

                t._GPIO = new GpioPin[gpioNumber];
                for (int i = 0; i < gpioNumber; i++)
                {
                    /*ID,  GPIO status in tag read metadata
                    GPO status in Low nibble (Bits 0 to 3) and (GPI status in High nibble (Bits 4 to 7) */
                    (t._GPIO)[i] = new GpioPin((i + 1), (((gpioByte >> i) & 0x1) == 1), ((gpioByte >> (i + 4)) & 0x1) == 1);
                }
            }
            if (protocol == TagProtocol.GEN2)
            {
                Gen2.TagReadData gen2 = new Gen2.TagReadData();
                t.prd = gen2;

                if (0 != (metadataFlags & TagMetadataFlag.GEN2_Q))
                {
                    gen2._q.InitialQ = response[readOffset++];
                }

                if (0 != (metadataFlags & TagMetadataFlag.GEN2_LF))
                {
                    switch (response[readOffset++])
                    {
                        case 0x00:
                            gen2._lf = Gen2.LinkFrequency.LINK250KHZ;
                            break;
                        case 0x02:
                            gen2._lf = Gen2.LinkFrequency.LINK320KHZ;
                            break;
                        case 0x04:
                            gen2._lf = Gen2.LinkFrequency.LINK640KHZ;
                            break;
                    }
                }

                if (0 != (metadataFlags & TagMetadataFlag.GEN2_TARGET))
                {
                    switch (response[readOffset++])
                    {
                        case 0x00:
                            gen2._target = Gen2.Target.A;
                            break;
                        case 0x01:
                            gen2._target = Gen2.Target.B;
                            break;
                    }
                }
                if (0 != (metadataFlags & TagMetadataFlag.BRAND_IDENTIFIER))
                {
                    byte[] brandID = new byte[2];
                    Array.Copy(response, readOffset, brandID, 0, 2);
                    t._brand_Identifier = ByteFormat.ToHex(brandID);
                    readOffset += 2;
                }
            }
            if (0 != (metadataFlags & TagMetadataFlag.TAGTYPE))
            {
                /* Tag Type response is in EBV format. Hence the number of bytes is not fixed. 
                 *  Number of bytes to extract depends on the MSB of the retrieved byte.Hence parse EBV data.
                 */
                byte[] tagType = parseEBVData(response, ref readOffset);

                //Convert from EBV format to actual tag type.
                t._tagType = ConvertFromEBV(tagType);
            }

            // Parsing the Correct Antenna ID based on GPO status
            if ((0 != (metadataFlags & TagMetadataFlag.ANTENNAID)))
            {
                if (!autonomousStreaming)
                {
                    if (t._GPIO != null)
                    {
                        if (t._GPIO.Length > 2)
                        {
                            if ((_version.Hardware.Part1 != MODEL_M6E_NANO) && (t._GPIO[2].High && !t._GPIO[3].High && ((portmask & 0x04) != 0x00)))
                                t._antenna += 16;
                            if ((_version.Hardware.Part1 != MODEL_M6E_NANO) && (!t._GPIO[2].High && t._GPIO[3].High && ((portmask & 0x08) != 0x00)))
                                t._antenna += 32;
                            if ((_version.Hardware.Part1 != MODEL_M6E_NANO) && (t._GPIO[2].High && t._GPIO[3].High && ((portmask & 0x0C) != 0x00)))
                                t._antenna += 48;
                        }
                    }
                    if (isTxRxMapSet)
                    {
                        int[] txrx = GetDefaultTxRx(t._antenna);
                        int antId = _txRxMap.GetDefaultAntenna(txrx);
                        if (antId != 0)
                        {
                            t._antenna = antId;
                        }
                    }
                }
            }
        }
        #region TranslateDefaultSerialAntenna

        /// <summary>
        /// Translate antenna ID from M5e Get Tag Buffer command to logical antenna number
        /// </summary>
        /// <param name="serant">M5e serial protocol antenna ID (TX: 4 msbs, RX: 4 lsbs)</param>
        /// <returns>Logical antenna number</returns>
        public int TranslateDefaultSerialAntenna(byte serant)
        {
            try
            {
                return GetDefaultVirt(serant);
            }
            catch (ArgumentException)
            {
                int tx = (serant >> 4) & 0xF;
                int rx = (serant >> 0) & 0xF;
                throw new ReaderParseException(String.Format(
                    "Can't find mapping from txrx({0:D},{1:D}) to logical antenna number",
                    tx, rx));
            }
        }

        #endregion

        #region TranslateSerialAntenna

        /// <summary>
        /// Translates the serial antenna
        /// </summary>
        /// <param name="txrx"></param>
        /// <returns></returns>
        public int TranslateSerialAntenna(byte txrx)
        {
            int tx = 0;
            if (((txrx >> 4) & 0xF) == ((txrx >> 0) & 0xF))
            {
                tx = (txrx >> 4) & 0xF;
            }
            return tx;
        }

        #endregion

        #region GetDefaultVirt

        /// <summary>
        /// Map (tx,rx) ports to virtual antenna
        /// </summary>
        /// <param name="txrx">(txport,rxport)</param>
        /// <returns>virtual antenna number</returns>
        public int GetDefaultVirt(byte txrx)
        {
            if (!defaultReverseMap.ContainsKey(txrx))
            {
                throw new ArgumentException(String.Format(
                    "No logical antenna mapping: txrx({0:D},{1:D})",
                    (txrx >> 4) & 0xF, (txrx >> 0) & 0xF));
            }
            return defaultReverseMap[txrx];
        }

        #endregion

        #region GetDefaultTxRx

        /// <summary>
        /// Map virtual antenna  to (tx,rx) ports
        /// </summary>
        /// <param name="virt">Virtual antenna number</param>
        /// <returns>(txport,rxport)</returns>
        public int[] GetDefaultTxRx(int virt)
        {
            if (!defaultForwardMap.ContainsKey(virt))
                throw new ArgumentException("Invalid logical antenna number: " + virt.ToString());

            return defaultForwardMap[virt];
        }

        #endregion

        #region ParseTagMemBankData
        /// <summary>
        /// Parse mem bank data
        /// </summary>
        /// <param name="t"></param>
        /// <param name="response"></param>
        /// <param name="readOffset"></param>
        private void ParseTagMemBankdata(ref TagReadData t, byte[] response, int readOffset)
        {
            int dataLength = t.Data.Length;
            while (dataLength != 0)
            {
                if (readOffset >= dataLength)
                    break;
                int bank = ((response[readOffset] >> 4) & 0x1F);
                int epcdataLength = response[readOffset + 1] * 2;
                switch (bank)
                {
                    case (int)Gen2.Bank.EPC:
                        t._dataEPCMem = new byte[epcdataLength];
                        Array.Copy(response, (readOffset + 2), t._dataEPCMem, 0, epcdataLength);
                        readOffset += (2 + epcdataLength);
                        break;
                    case (int)Gen2.Bank.RESERVED:
                        t._dataReservedMem = new byte[epcdataLength];
                        Array.Copy(response, (readOffset + 2), t._dataReservedMem, 0, epcdataLength);
                        readOffset += (2 + epcdataLength);
                        break;
                    case (int)Gen2.Bank.TID:
                        t._dataTidMem = new byte[epcdataLength];
                        Array.Copy(response, (readOffset + 2), t._dataTidMem, 0, epcdataLength);
                        readOffset += (2 + epcdataLength);
                        break;
                    case (int)Gen2.Bank.USER:
                        t._dataUserMem = new byte[epcdataLength];
                        Array.Copy(response, (readOffset + 2), t._dataUserMem, 0, epcdataLength);
                        readOffset += (2 + epcdataLength);
                        break;
                    default: break;
                }
            }
        }
        #endregion

        #endregion

        #region ParseTagData

        /// <summary>
        /// Parse one tag data record out of GetTagBuffer response.
        /// Input Format: lenhi lenlo data... [pad...]
        /// </summary>
        /// <param name="response">GetTagBuffer response</param>
        /// <param name="readOffset">Index of start of record.  Will be updated to point after record.</param>
        /// <param name="protocol"></param>
        /// <param name="reclen">Length of record, in bytes, not including length field.
        /// e.g., 16 for 16-bit PC + 96-bit EPC + 16-bit CRC
        /// e.g., 66 for 16-bit PC + 496-bit EPC + 16-bit CRC
        /// Specify 0 for variable-length records.</param>
        /// <returns></returns>

        private static TagData ParseTagData(byte[] response, ref int readOffset, TagProtocol protocol, int reclen)
        {
            int idlenbits = ByteConv.ToU16(response, ref readOffset);
            int datastartOffset = readOffset;
            List<byte> pcList = new List<byte>();

            if (0 != (idlenbits % 8)) throw new ReaderParseException("EPC length not a multiple of 8 bits");
            int idlen = idlenbits / 8;
            TagData td = null;
            int crclen = 2;
            int epclen = idlen;
            if (!(TagProtocol.ATA == protocol || TagProtocol.ISO14443A == protocol || TagProtocol.ISO14443B == protocol|| TagProtocol.ISO15693 == protocol || TagProtocol.LF125KHZ == protocol || TagProtocol.LF134KHZ == protocol))
            {
                if (epclen >= crclen)
                {
                    epclen = idlen - crclen;
                }
            }

            if (TagProtocol.GEN2 == protocol)
            {
                int pclen = 2;
                pcList.AddRange(SubArray(response, ref readOffset, pclen));
                if (epclen >= pclen)
                {
                    epclen -= pclen;
                }
                /* Add support for XPC bits
                 * XPC_W1 is present, when the 6th most significant bit of PC word is set
                 */
                if ((pcList[0] & 0x02) == 0x02)
                {
                    /* When this bit is set, the XPC_W1 word will follow the PC word
                     * Our TMR_Gen2_TagData::pc has enough space, so copying to the same.
                     */
                    if (readOffset < response.Length)
                    {
                        byte[] xpcW1 = SubArray(response, ref readOffset, 2);
                        pcList.AddRange(xpcW1);
                        pclen += 2;  /* PC bytes are now 4*/
                        epclen -= 2; /* EPC length will be length - 4(PC + XPC_W1)*/
                    }
                    /*
                     * If the most siginificant bit of XPC_W1 is set, then there exists
                     * XPC_W2. A total of 6  (PC + XPC_W1 + XPC_W2 bytes)
                     */
                    if ((pcList[2] & 0x80) == 0x80)
                    {
                        if (readOffset < response.Length)
                        {
                            byte[] xpcW2 = SubArray(response, ref readOffset, 2);
                            pcList.AddRange(xpcW2);
                            pclen += 2;  /* PC bytes are now 6 */
                            epclen -= 2; /* EPC length will be length - 6 (PC + XPC_W1 + XPC_W2)*/
                        }
                    }
                }
            }
            byte[] pc = pcList.ToArray();
            byte[] epc;
            byte[] crc = new byte[2] { 0xff, 0xff };
            byte[] xepc = new byte[2];
            try
            {
                // Handling NegativeArraySizeException
                // Ignoring invalid tag response (epcLen goes to negative), which disturbs further parsing of tagresponse
                // example below
                // 0xFFDC29000001FF000714E3110E0CAE0000001200890500000C00300800DEADAC930
                // 6DA110E0CAE00000016007E0500000C0080300052DF9DFE14F5B6F0010100707F4F
                // *** Corrupt data in tag response(Looks like PC word in some of these tags got corrupted with XPC bit)
                // 01 A6 11 0E0CAE 00000019 005F 05 0000 0C 0020 06206C34
                // ***
                // 05D4110E0CAE0000001A00890500000C00803400DEADCAFEDEADCAFEDEADCAFE80691
                // 2CF110E0CAE00000022008C0500000C0080300043215678DEADCAFEDEADCAFEFDB11D
                // DF110E0CAE0000002300A00500000C00803000300833B2DDD901400000000039BB0BE
                // 3110E0CAE0000002C00970500000C00803000DEADBEEFDEADBEEFDEADBEEF9C1E
                // Even if EPC length is 4 bytes and PC word is corrupted with XPC bit
                if (epclen > -1)
                {
                    if (0 != (metadataFlags & TagMetadataFlag.BRAND_IDENTIFIER))
                    {
                        epclen -= 2;
                    }
                    epc = SubArray(response, ref readOffset, epclen);
                    if (0 != (metadataFlags & TagMetadataFlag.BRAND_IDENTIFIER))
                    {
                        xepc = SubArray(response, ref readOffset, 2);
                    }
                }
                else
                {
                    epc = null;
                }
                if ((crclen > -1) && (epclen > -1))
                {
                    if (TagProtocol.ATA != protocol && TagProtocol.ISO14443A != protocol && TagProtocol.ISO14443B != protocol && TagProtocol.ISO15693 != protocol && TagProtocol.LF125KHZ != protocol && TagProtocol.LF134KHZ != protocol)
                    {
                        crc = SubArray(response, ref readOffset, crclen);
                    }
                }
                else
                {
                    crc = null;
                    if (TagProtocol.GEN2 == protocol)
                    {
                        // if PC word got corrupted with XPC bit i.e XPC_W1 and XPC_W2
                        // if pc.Length = 4, PC word is followed by XPC_W1.
                        // if pc.Length = 6 , PC word is followed by XPC_W1 and XPC_W2
                        if (epclen < -2)
                        {
                            readOffset = readOffset - (pc.Length + (epclen));
                        }
                    }
                }

                switch (protocol)
                {
                    case TagProtocol.GEN2:
                        if (0 != (metadataFlags & TagMetadataFlag.BRAND_IDENTIFIER))
                        {
                            td = new Gen2.TagData(epc, crc, pc, xepc);
                        }
                        else
                        {
                            td = new Gen2.TagData(epc, crc, pc);
                        }
                        break;
                    case TagProtocol.IPX64:
                        td = new Ipx64.TagData(epc, crc);
                        break;
                    case TagProtocol.IPX256:
                        td = new Ipx256.TagData(epc, crc);
                        break;
                    case TagProtocol.ISO180006B:
                        td = new Iso180006b.TagData(epc, crc);
                        break;
                    case TagProtocol.ISO180006B_UCODE:
                        td = new Iso180006bUcode.TagData(epc, crc);
                        break;
                    case TagProtocol.ATA:
                        td = new Ata.TagData(epc, crc);
                        break;
                    case TagProtocol.ISO14443A:
                        td = new Iso14443a.TagData(epc);
                        break;
                    case TagProtocol.ISO14443B:
                        td = new Iso14443b.TagData(epc);
                        break;
                    case TagProtocol.ISO15693:
                        td = new Iso15693.TagData(epc);
                        break;
                    case TagProtocol.LF125KHZ:
                        td = new Lf125khz.TagData(epc);
                        break;
                    case TagProtocol.LF134KHZ:
                        td = new Lf134khz.TagData(epc);
                        break;
                    default:
                        td = new TagData(epc, crc);
                        break;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            if (0 < reclen) { readOffset = datastartOffset + reclen; }
            return td;
        }

        #endregion

        #region PrepForSearch

        /// <summary>
        /// Configure reader for tag search
        /// </summary>
        /// <param name="rp">Simple Read Plan</param>
        /// <param name="timeOut">timeOut</param>
        /// <returns>Search flag value for antenna control bits (2 lsbs) of Read Tag Multiple (0x22) command</returns>
        private AntennaSelection PrepForSearch(ReadPlan rp, int timeOut)
        {
            AntennaSelection flags = 0;

            if (rp is SimpleReadPlan)
            {
                SimpleReadPlan srp = (SimpleReadPlan)rp;
                int[] selectedAnts = srp.Antennas;
                if ((null == selectedAnts) || (0 == selectedAnts.Length))
                {
                    // isPerAntTimeSet flag is set meaning Antenna to search on is already configured.It uses TMR_PARAM_PER_ANTENNA_TIME time based antenna search.
                    // Otherwise fallback to existing implementation of autodetect using getConnectedAntennas().
                    if (!isPerAntTimeSet)
                    {
                        selectedAnts = (int[])ParamGet("/reader/antenna/connectedPortList");

                        if (0 == selectedAnts.Length)
                        {
                            throw new ArgumentException("No valid antennas specified");
                        }
                    }
                    else
                    {
                        if (model.Equals("M3e"))
                        {
                            flags = AntennaSelection.CONFIGURED_ANTENNA;
                        }
                        else
                        {
                            flags = AntennaSelection.CONFIGURED_LIST;
                        }
                    }

                }
                //For consistency with Java and C
                //If antenna is not selected, any way using connectedPortList, so commentting below code
                //else if (1 == selectedAnts.Length)
                //{
                //    flags = AntennaSelection.CONFIGURED_ANTENNA;
                //    SetLogicalAntenna(selectedAnts[0]);
                //}
                else
                {
                    if (model.Equals("M3e"))
                    {
                        flags = AntennaSelection.CONFIGURED_ANTENNA;
                    }
                    else
                    {
                        flags = AntennaSelection.CONFIGURED_LIST;
                    }
                    SetLogicalAntennaList(selectedAnts);
                }
            }
            else if (rp is MultiReadPlan)
            {
                _searchList = null;
                MultiReadPlan mrp = (MultiReadPlan)rp;
                if (validateParams(mrp) && (featureflag.Contains(ReaderFeaturesFlag.READER_FEATURES_FLAG_ANTENNA_READ_TIME)))
                {
                    flags = AntennaSelection.CONFIGURED_LIST;
                    CmdSetAntennaReadTimeList(rp, timeOut);
                }
                else
                {
                    flags = PrepForSearch((SimpleReadPlan)mrp.Plans[0], timeOut);
                }
            }
            else
            {
                throw new ArgumentException("Invalid Read Plan Type");
            }

            return flags;
        }

        #endregion

        #region PrepForTagop

        /// <summary>
        /// Configure reader for synchronous tag operation (e.g., set appropriate protocol and antenna)
        /// </summary>
        public void PrepForTagop()
        {
            int ant = (int)ParamGet("/reader/tagop/antenna");

            if (0 == ant)
                throw new ReaderException("No antenna detected or selected for tag operations");
            else
                SetLogicalAntenna(ant);

            setProtocol((TagProtocol)ParamGet("/reader/tagop/protocol"));
        }

        #endregion

        #region PrepForEmbeddedKillTag
        /// <summary>
        /// Prepare byte array for Embedded Kill Tag
        /// </summary>
        /// <param name="timeout">Length of time to retry kill (milliseconds)</param>
        /// <param name="password">Tag's kill password</param>
        /// <param name="sing">Select options</param>
        private static byte[] PrepForEmbeddedKillTag(UInt16 timeout, UInt32 password, SingulationBytes sing)
        {
            List<byte> list = new List<byte>();
            list.Add(0x26);
            list.AddRange(ByteConv.EncodeU16(timeout));
            list.Add(sing.Option);
            list.AddRange(ByteConv.EncodeU32(password));
            list.Add(0x00); // RFU
            list.AddRange(sing.Mask);
            return list.ToArray();
        }
        #endregion

        #region PrepareForEmbeddedLockTag

        /// <summary>
        /// Prepare byte array for embedded command with lock tag.
        /// </summary>
        /// <param name="timeout">Number of milliseconds to keep retrying operation</param>
        /// <param name="accessPassword">Tag access password</param>
        /// <param name="mask">Lock Mask</param>
        /// <param name="action">Lock action to take</param>
        /// <param name="sing">Select options for tag to act on</param>
        /// <returns>Returns data array for Read Tag and Lock Multiple</returns>
        private static byte[] PrepareForEmbeddedLockTag(UInt16 timeout, UInt32 accessPassword, UInt16 mask, UInt16 action, SingulationBytes sing)
        {
            List<byte> list = new List<byte>();
            list.Add(0x25);
            list.AddRange(ByteConv.EncodeU16(timeout));
            list.Add(sing.Option);
            list.AddRange(ByteConv.EncodeU32(accessPassword));
            list.AddRange(ByteConv.EncodeU16(mask));
            list.AddRange(ByteConv.EncodeU16(action));
            list.AddRange(sing.Mask);
            return list.ToArray();
        }
        #endregion

        #region PrepareForEmbeddedWriteTagData

        /// <summary>
        /// Prepares byte array for embedded command with Write Data
        /// </summary>
        /// <param name="timeout">Timeout</param>
        /// <param name="memBank">Memory Bank to Write</param>
        /// <param name="address">Address in Memory Bank to Write</param>
        /// <param name="data">Data to Write</param>
        /// <param name="singulation">Singulation Filer</param>
        /// <returns>Byte Array of Write Data</returns>
        private static byte[] PrepareForEmbeddedWriteTagData(UInt16 timeout, byte memBank, UInt32 address, byte[] data, SingulationBytes singulation)
        {
            if (IsOdd(data.Length))
                throw new ArgumentException("Number of bytes (" + data.Length.ToString() + ") must be even for Gen2 Tag Write Data");

            List<byte> list = new List<byte>();
            list.Add(0x24);
            list.AddRange(ByteConv.EncodeU16(timeout));
            list.Add(singulation.Option);
            list.AddRange(ByteConv.EncodeU32(address));
            list.Add(memBank);
            list.AddRange(singulation.Mask);
            list.AddRange(data);

            return list.ToArray();
        }

        #endregion

        #region PrepareForEmbeddedReadTagData

        /// <summary>
        /// Prepare byte array for Read Tag Id and Data multiple.
        /// </summary>
        /// <param name="timeout">Timeout</param>
        /// <param name="memBank">Memory Bank</param>
        /// <param name="address">Adress to read Data</param>
        /// <param name="wordcount">Number of Words to read</param>
        /// <param name="singulation">Singulation Bytes</param>
        /// <returns>Byte array Read Data</returns>
        private static byte[] PrepareForEmbeddedReadTagData(UInt16 timeout, byte memBank, UInt32 address, byte wordcount, SingulationBytes singulation)
        {
            List<byte> list = new List<byte>();
            list.Add(0x28);
            list.AddRange(ByteConv.EncodeU16(timeout));
            list.Add(singulation.Option);
            list.Add(memBank);
            list.AddRange(ByteConv.EncodeU32(address));
            list.Add(wordcount);
            list.AddRange(singulation.Mask);

            return list.ToArray();
        }

        #endregion

        #region M5eToEmdCmdWithMultipleReadFormat

        /// <summary>
        /// Parses M5e response to embedded command with multiple tag read format
        /// </summary>
        /// <param name="response">byte array of response from M5e</param>
        /// <returns>int array of the form (Number of tags Read, Number of Suceeded Operations, Number of Failed Operations)</returns>
        private static int[] M5eToEmdCmdWithMultipleReadFormat(byte[] response)
        {
            int ptr = 3;
            List<int> returnResponse = new List<int>();
            returnResponse.Add((int)response[ptr++]);
            ptr += 2;
            returnResponse.Add((int)ByteConv.ToU16(response, ptr));
            ptr += 2;
            returnResponse.Add((int)ByteConv.ToU16(response, ptr));

            return returnResponse.ToArray();
        }
        #endregion

        #endregion

        #region GPIO Commands

        #region GpiGet

        /// <summary>
        /// Get the state of all of the reader's GPI pins. 
        /// </summary>
        /// <returns>array of GpioPin objects representing the state of all input pins</returns>
        public override GpioPin[] GpiGet()
        {
            List<GpioPin> pinvals = new List<GpioPin>();
            GpioPin[] states = CmdGetGpio();

            foreach (GpioPin state in states)
            {
                if (!state.Output)
                {
                    // only add input pins
                    pinvals.Add(new GpioPin(state.Id, state.High));
                }
            }
            return pinvals.ToArray();
        }

        #endregion

        #region GpoSet

        /// <summary>
        /// Set the state of some GPO pins.
        /// </summary>
        /// <param name="state">array of GpioPin objects</param>
        public override void GpoSet(ICollection<GpioPin> state)
        {
            foreach (GpioPin gp in state)
            {
                CmdSetGpio((byte)gp.Id, gp.High);
            }
        }

        #endregion

        #region CmdSetGpio

        /// <summary>
        /// Set the state of a single GPIO pin
        /// </summary>
        /// <param name="GPIOnumber">the gpio pin number</param>
        /// <param name="GPIOvalue">whether to set the pin high</param>
        [Obsolete()]
        public void CmdSetGpio(byte GPIOnumber, bool GPIOvalue)
        {
            byte[] data = new byte[3];
            data[0] = 0x96;
            data[1] = GPIOnumber;
            data[2] = BoolToByte(GPIOvalue);
            byte[] inputBuffer = SendM5eCommand(data);
        }
        #endregion

        #region CmdGetGpio

        /// <summary>
        /// Gets the state of the device's GPIO pins.
        /// </summary>
        /// <returns>
        /// an array of GpioPin representing the state of each pin
        /// with the pin id, direction and value
        /// </returns>
        [Obsolete()]
        public GpioPin[] CmdGetGpio()
        {
            List<GpioPin> gpioStatus = new List<GpioPin>();
            List<byte> data = new List<byte>();
            int len;

            data.Add(0x66);
            data.Add(0x01);

            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));

            int offset = 1;
            len = (response.Length - 1) / 3;
            for (int i = 0; i < len; i++)
            {
                int id = response[offset++];
                bool dir = (1 == response[offset++]) ? true : false;
                bool value = (1 == response[offset++]) ? true : false;
                gpioStatus.Add(new GpioPin(id, value, dir));
            }

            return gpioStatus.ToArray();
        }

        #endregion

        #region CmdGetGPIODirection

        ///<summary>
        /// Get direction of a single GPIO pin
        ///</summary>
        ///<param name = "pin">GPIO pin Number</param>
        ///<returns>
        /// true if output pin, false if input pin
        ///</returns>
        [Obsolete()]
        public bool CmdGetGPIODirection(int pin)
        {
            bool direction;
            byte[] data = new byte[2];
            data[0] = 0x96;
            data[1] = (byte)pin;
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(data));
            direction = (response[1] == 1);
            return direction;
        }

        #endregion

        #region CmdSetGPIODirection

        ///<summary>
        /// Set direction of a single GPIO pin
        ///</summary>
        ///<param name = "pin">GPIO pin Number</param>
        ///<param name = "direction">GPIO pin direction</param>
        [Obsolete()]
        public void CmdSetGPIODirection(int pin, bool direction)
        {
            byte[] data = new byte[5];
            data[0] = 0x96;
            data[1] = 0x01; //option flag
            data[2] = (byte)pin;
            data[3] = (byte)(direction ? 1 : 0);
            data[4] = 0;
            SendM5eCommand(data);
        }

        #endregion

        #region getGPIODirection

        ///<summary>
        ///Get directions of all GPIO pins
        ///</summary>
        ///<param name = "wantOut"> false = get inputs, true = get outputs///</param>
        ///<returns> list of pins that are set in the requested direction
        ///</returns>

        public int[] getGPIODirection(bool wantOut)
        {
            int[] retval;
            byte gpioDirections = (byte)0xFF;
            List<int> pinList = new List<int>();
            if ((M6eFamilyList.Contains(model)) || (model.Equals("M3e")))
            {
                // Get supported gpio pins
                GpioPin[] gpioPins = CmdGetGpio();

                if ((byte)0xFF == gpioDirections)
                {
                    /* Cache the current state */
                    gpioDirections = 0;
                    for (int pin = 1; pin <= gpioPins.Length; pin++)
                    {
                        if (CmdGetGPIODirection(pin))
                        {
                            gpioDirections = (byte)(gpioDirections | (1 << pin));
                        }
                    }
                }

                for (int pin = 1; pin <= gpioPins.Length; pin++)
                {
                    bool bitTest = ((gpioDirections >> pin & 1) == 1);
                    if (wantOut == bitTest)
                    {
                        pinList.Add(pin);
                    }
                }
                retval = pinList.ToArray();
            }
            else
            {
                retval = new int[] { 1, 2 };
            }

            return retval;
        }

        #endregion

        #region setGPIODirection
        ///<summary>
        /// Set directions of all GPIO pins
        ///</summary>
        ///<param name = "wantOut"> false = input, true = output</param>
        ///<param name = "pins">GPIO pins to set to the desired direction. All other pins implicitly          ///set the other way.</param>

        public void setGPIODirection(bool wantOut, int[] pins)
        {
            byte newDirections;
            if ((M6eFamilyList.Contains(model)) || (model.Equals("M3e")))
            {
                if (wantOut)
                {
                    newDirections = 0;
                }
                else
                {
                    newDirections = 0x1e;
                }

                for (int i = 0; i < pins.Length; i++)
                {
                    int bit = 1 << pins[i];
                    if (wantOut)
                    {
                        newDirections = (byte)(newDirections | bit);
                    }
                    else
                    {
                        newDirections = (byte)(newDirections & ~(bit));
                    }
                }

                for (int pin = 0; pin < pins.Length; pin++)
                {
                    int bit = 1 << pins[pin];
                    bool direction = ((newDirections & bit) != 0);
                    CmdSetGPIODirection(pins[pin], direction);
                }
            }
            else
            {
                throw new ArgumentException("Parameter is read only.");
            }
        }

        #endregion

        #region cmdGetPerAntennaTime
        /// <summary>
        /// Gets the antenna time of each port.
        /// </summary>
        /// <returns>Return an array of 2-element arrays of integers interpreted as(tx port, antenna time in ms)./nAn example with two antenna ports, antenna time is given below: {{1, 500}, {0, 250}}. Here port '0' indicates off time.</returns>
        public int[][] cmdGetPerAntennaTime()
        {
            try
            {
                List<int[]> list = new List<int[]>();
                byte[] cmd = new byte[] { 0x61, 0x07 };
                byte[] response = GetDataFromM5eResponse(SendM5eCommand(cmd));
                byte[] result = new byte[response.Length - 1];
                if (response.Length > 1)
                {
                    Array.Copy(response, 1, result, 0, (response.Length - 1));
                    int count = result.Length / 3;
                    int offset = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int ant = result[offset++];
                        int time = (int)ByteConv.ToU16(result, offset);
                        offset += 2;
                        list.Add(new int[] { ant, time });
                    }
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }

        #endregion

        #region cmdSetPerAntennaTime
        /// <summary>
        /// This method sets the antenna read time(on and off time) in the list
        /// </summary>
        public void cmdSetPerAntennaTime(int[][] list)
        {
            try
            {
                List<byte> cmd = new List<byte>();
                cmd.Add((byte)(0x91));
                cmd.Add((byte)(0x07));

                for (int i = 0; i < list.Length; i++)
                {
                    cmd.Add((byte)list[i][0]);
                    if (list[i][1] <= 0xFFFF)
                    {
                        cmd.AddRange(ByteConv.EncodeU16(Convert.ToUInt16(list[i][1])));
                    }
                }

                byte[] response = SendM5eCommand(cmd);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }
        #endregion

        #region Reader Statistic Commands

        #region CmdGetReaderStatistics
        /// <summary>
        /// Get the current per-port statistics.
        /// </summary>
        /// <param name="statsFlag">the set of statistics to gather</param>
        /// <returns>
        /// a ReaderStatistics structure populated with the requested per-port
        /// values
        /// </returns>        
        private ReaderStatistics CmdGetReaderStatistics(ReaderStatisticsFlag statsFlag)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x6C);
            cmd.Add(0x02);//option byte per port 0x02
            cmd.Add((byte)statsFlag);
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(cmd));
            ReaderStatistics returnResponse = new ReaderStatistics();
            int offset = 2;
            int length = _txRxMap.ValidAntennas.Length;
            List<UInt32> tmpRfOnTime = new List<UInt32>();
            List<byte> tmpNoiseFloor = new List<byte>();
            List<byte> tmpNoiseFloorTxOn = new List<byte>();
            int port = 0;
            while (offset < response.Length - 2)
            {
                if (0 != (response[offset] & (byte)ReaderStatisticsFlag.RF_ON_TIME))
                {
                    offset += 2;
                    for (int i = 0; i < length; i++)
                    {
                        port = response[offset];
                        if (i == (port - 1))
                        {
                            offset++;
                            tmpRfOnTime.Add(ByteConv.ToU32(response, offset));
                            offset += 4;
                        }
                        else
                        {
                            tmpRfOnTime.Add(0);
                        }
                    }
                    returnResponse.rfOnTime = tmpRfOnTime.ToArray();
                    returnResponse.numPorts = length;
                }
                else if (0 != (response[offset] & (byte)ReaderStatisticsFlag.NOISE_FLOOR))
                {
                    offset += 2;
                    for (int i = 0; i < length; i++)
                    {
                        port = response[offset];
                        if (i == (port - 1))
                        {
                            offset++;
                            tmpNoiseFloor.Add(response[offset]);
                            offset++;
                        }
                        else
                        {
                            tmpNoiseFloor.Add(0);
                        }
                    }
                    returnResponse.noiseFloor = tmpNoiseFloor.ToArray();
                    returnResponse.numPorts = length;
                }
                else if (0 != (response[offset] & (byte)ReaderStatisticsFlag.NOISE_FLOOR_TX_ON))
                {
                    offset += 2;
                    for (int i = 0; i < length; i++)
                    {
                        port = response[offset];
                        if (i == (port - 1))
                        {
                            offset++;
                            tmpNoiseFloorTxOn.Add(response[offset]);
                            offset++;
                        }
                        else
                        {
                            tmpNoiseFloorTxOn.Add(0);
                        }
                    }
                    returnResponse.noiseFloorTxOn = tmpNoiseFloorTxOn.ToArray();
                    returnResponse.numPorts = length;
                }
            }
            return returnResponse;
        }

        #endregion

        #region CmdResetReaderStatistics

        /// <summary>
        /// Reset the per-port statistics.
        /// </summary>
        /// <param name="statsFlag">the set of statistics to reset. Only the RF on time statistic may be reset.</param>
        [Obsolete()]
        public void CmdResetReaderStatistics(ReaderStatisticsFlag statsFlag)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x6C);
            cmd.Add(0x01);
            cmd.Add((byte)statsFlag);
            SendM5eCommand(cmd);
        }

        #endregion

        #region CmdResetReaderStats

        /// <summary>
        /// Reset the per-port stats.
        /// </summary>
        /// <param name="StatsFlag">the set of stats to reset. Only the RF on time statistic may be reset.</param>
        private void CmdResetReaderStats(Reader.Stat StatsFlag)
        {
            Reader.Stat.StatsFlag statsFlag;
            if (model == "M3e")
            {
                statsFlag = StatsFlag.RESETM3EREADERSTATS;
            }
            else
            {
                statsFlag = StatsFlag.RESETREADERSTATS;
            }
            List<byte> cmd = new List<byte>();
            cmd.Add(0x6C);
            cmd.Add(0x01);
            GetExtendedReaderStatsFlag(ref cmd, statsFlag);
            SendM5eCommand(cmd);
        }

        #endregion

        #region Reader Stats Commands

        #region GetExtendedReaderStatsFlag

        /// <summary>
        /// To extend the flag byte, an EBV technique is to be used. When the highest order bit of the flag
        /// is used, it signals the reader's parser, that another flag byte is to follow.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="flag"></param>
        private void GetExtendedReaderStatsFlag(ref List<byte> cmd, Reader.Stat.StatsFlag flag)
        {
            // convert flags to EBV format
            List<byte> flagsConverted = ConvertToEBV(flag);
            cmd.AddRange(flagsConverted);
        }

        #endregion

        #region CmdGetReaderStats
        /// <summary>
        /// Get the current per-port statistics.
        /// </summary>
        /// <param name="statsFlag">the set of statistics to gather</param>
        /// <returns>
        /// a ReaderStats structure populated with the requested per-port
        /// values
        /// </returns>
        private Reader.Stat.Values CmdGetReaderStats(Reader.Stat.StatsFlag statsFlag)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x6C);
            cmd.Add(0x02);//option byte per port 0x02
            GetExtendedReaderStatsFlag(ref cmd, statsFlag);
            int offset = 2;
            if ((0x80) > Convert.ToUInt16(statsFlag))
            {
                offset = 2;
            }
            else if ((0x4000) > Convert.ToUInt32(statsFlag))
            {
              offset += 1;
            }
            else
            {
                offset += 2;
            }
            byte[] response = GetDataFromM5eResponse(SendM5eCommand(cmd));
            Reader.Stat.Values returnResponse = ParseReaderStatValues(response, offset, Convert.ToUInt16(statsFlag));

            return returnResponse;
        }

        #endregion

        #region ParseReaderStatValues

        /// <summary>
        /// Fill reader stat values from response
        /// </summary>
        /// <param name="response"></param>
        /// <param name="offset"></param>
        /// <param name="flag"></param>
        /// <returns></returns>
        private Reader.Stat.Values ParseReaderStatValues(byte[] response, int offset, UInt16 flag)
        {
            Reader.Stat.Values values = new Stat.Values();
            List<Stat.PerAntennaValues> perAntennaValuesList = new List<Stat.PerAntennaValues>();
            if (!(autonomousStreaming))
            {
                for (int j = 0; j < _txRxMap.ValidAntennas.Length; j++)
                {
                    perAntennaValuesList.Add(new Stat.PerAntennaValues());
                }
                values.PERANTENNA = perAntennaValuesList;
            }
            int i;
            while (offset < (response.Length))
            {
                // parse the flags received to retrieve the actual flag. Some flags might be in EBV format while others not.
                // parseEBVData() parses the flags and modifies the readoffset based on the flag. Hence no need to skip the flag bytes in each case.
                byte[] flagsValue = parseEBVData(response, ref offset);
                flag = (ushort)ConvertFromEBV(flagsValue);
                Reader.Stat.StatsFlag tempStatFlag = (Reader.Stat.StatsFlag)flag;
                if ((tempStatFlag & Reader.Stat.StatsFlag.RFONTIME) != 0)
                {
                    int length = response[offset++];
                    for (i = 0; i < _txRxMap.ValidAntennas.Length; i++)
                    {
                        if (length != 0)
                        {
                            Stat.PerAntennaValues perAntennaValues = new Stat.PerAntennaValues();
                            // Since TranslateSerialAntenna method requires txrx port, Oring rx port same as tx port. 
                            // Because reader sends only tx port in response.
                            ushort antennaID = (ushort)_txRxMap.TranslateSerialAntenna((byte)((response[offset] << 4) | response[offset]));
                            values.PERANTENNA[antennaID - 1].Antenna = antennaID;
                            offset++;
                            values.PERANTENNA[antennaID - 1].RfOnTime = ByteConv.ToU32(response, offset);
                            offset += 4;
                        }
                    }
                    values.PERANTENNA = perAntennaValuesList;
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.NOISEFLOORSEARCHRXTXWITHTXON) != 0)
                {
                    int length = response[offset++];
                    for (i = 0; i < _txRxMap.ValidAntennas.Length; i++)
                    {
                        if (length != 0)
                        {
                            Stat.PerAntennaValues perAntennaValues = new Stat.PerAntennaValues();
                            byte TxPort = (byte)(ByteConv.GetU8(response, offset));
                            offset++;
                            byte RxPort = (byte)(ByteConv.GetU8(response, offset));
                            ushort antennaID = (ushort)_txRxMap.TranslateSerialAntenna((byte)((TxPort << 4) | RxPort));
                            values.PERANTENNA[antennaID - 1].Antenna = antennaID;
                            offset++;
                            values.PERANTENNA[antennaID - 1].NoiseFloor = (sbyte)ByteConv.GetU8(response, offset);
                            offset++;
                            length -= 3;
                        }
                    }
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.FREQUENCY) != 0)
                {
                    int length = response[offset++];
                    values.FREQUENCY = (uint)ByteConv.GetU24(response, offset);
                    offset += length;
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.TEMPERATURE) != 0)
                {
                    int length = response[offset++];
                    values.TEMPERATURE = (SByte)ByteConv.GetU8(response, offset);
                    offset += length;
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.PROTOCOL) != 0)
                {
                    int length = response[offset++];
                    values.PROTOCOL = (TagProtocol)Enum.Parse(typeof(TagProtocol), ByteConv.GetU8(response, offset).ToString(), false);
                    offset += length;
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.ANTENNAPORTS) != 0)
                {
                    int length = response[offset++];
                    for (i = 0; i < _txRxMap.ValidAntennas.Length; i++)
                    {
                        if (length != 0)
                        {
                            byte TxPort = (byte)(ByteConv.GetU8(response, offset));
                            offset++;
                            byte RxPort = (byte)(ByteConv.GetU8(response, offset));
                            values.ANTENNA = (ushort)_txRxMap.TranslateSerialAntenna((byte)((TxPort << 4) | RxPort));
                            offset++;
                            length -= 2;
                        }
                    }
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.CONNECTEDANTENNAS) != 0)
                {
                    uint[] connectedAntennas = new uint[response[offset++]];
                    List<uint> connectedPhysPortList = new List<uint>();
                    values.totalAntennaCount = ((int[])ParamGet("/reader/antenna/portList")).Length;
                    for (i = 0; i < connectedAntennas.Length; )
                    {
                        if (0 != response[++offset])
                        {
                            connectedPhysPortList.Add(response[--offset]);
                            offset += 2;
                        }
                        else
                        {
                            ++offset;
                        }
                        i += 2;
                    }
                    values.CONNECTEDANTENNA = connectedPhysPortList.ToArray();
                }
                else if ((tempStatFlag & Reader.Stat.StatsFlag.DCVOLTAGE) != 0)
                {
                    int length = response[offset++];
                    values.DCVOLTAGE = ByteConv.ToU16(response, offset);
                    offset += length;
                }
            }// End of while loop

            //Store the requested flags for future validation
            values.VALID = statFlag;
            if (!(autonomousStreaming))
            {
                List<Stat.PerAntennaValues> tempPerAntennaValuesList = new List<Stat.PerAntennaValues>();
                // Iterate through the per antenna values,
                // if found  any 0-antenna rows, don't add to the temporary list so that we can compact out the empty space.
                for (int index = 0; index < values.PERANTENNA.Count; index++)
                {
                    if (values.PERANTENNA[index].Antenna != 0)
                    {
                        tempPerAntennaValuesList.Add(values.PERANTENNA[index]);
                    }
                }
                values.PERANTENNA = null;
                values.PERANTENNA = tempPerAntennaValuesList;
            }

            return values;
        }

        #endregion

        #endregion

        #endregion

        #region Test Comands

        #region CmdTestSetFrequency

        /// <summary>
        /// Set the operating frequency of the device. Testing command.
        /// </summary>
        /// <param name="frequency">the frequency to set, in kHz</param>
        public void CmdTestSetFrequency(UInt32 frequency)
        {
            List<byte> data = new List<byte>();
            data.Add(0xC1);
            data.AddRange(ByteConv.EncodeU32(frequency));
            SendM5eCommand(data);
        }

        #endregion

        #region CmdTestSendCw

        /// <summary>
        /// Turn CW transmission on or off. Testing command.
        /// </summary>
        /// <param name="onOff">whether to turn CW on or off</param>
        public void CmdTestSendCw(bool onOff)
        {
            byte[] data = new byte[2];
            data[0] = 0xC3;
            data[1] = BoolToByte(onOff);
            SendM5eCommand(data);
        }

        #endregion

        #region CmdTestSendPrbs

        /// <summary>
        /// Turn on pseudo-random bit _stream transmission for a particular duration.  
        /// Testing command.
        /// </summary>
        /// <param name="duration">the duration to transmit the PRBS signal. Valid range is 0-65535</param>
        public void CmdTestSendPrbs(UInt16 duration)
        {
            byte[] data = new byte[4];
            data[0] = 0xC3;
            data[1] = 0x02;
            ByteConv.FromU16(data, 2, duration);
            SendM5eCommand(data);
        }

        #endregion

        #region
        /// <summary>
        /// Turn ON PRBS/CW transmission for a particular duration(timed) or continuously until turn OFF command is issued  
        /// Testing command.
        /// </summary>
        /// <param name="mode">the mode to operate on - Continuous or Time based</param>
        /// <param name="modulation">the modulation is either CW or PRBS</param>
        /// <param name="onTime">the duration to transmit the PRBS signal. Valid range is 0-65535</param>
        /// <param name="offTime">the duration to transmit the PRBS signal. Valid range is 0-65535</param>
        /// <param name="enable">enable flag which tells to turn ON or turn OFF CW/PRBS value is either true or false.</param>
        public void cmdTestSendRegulatoryTest(RegulatoryMode mode, RegulatoryModulation modulation, int onTime, int offTime, Boolean enable)
        {
            List<byte> data = new List<byte>();
            data.Add(0xC3);
            if (enable)
            {
                data.Add((byte)modulation);
                if (mode.ToString().ToUpper().Equals("CONTINUOUS"))
                {
                    data.AddRange(ByteConv.EncodeU16((ushort)0x0000));
                    //data.Add((byte)0x0000);
                }
                else
                {
                    // For timed mode, number of cycles set to 1.
                    data.AddRange(ByteConv.EncodeU16((ushort)0x0001));
                    //data.Add((byte)0x0001);
                }
                data.AddRange(ByteConv.EncodeU16((ushort)onTime));
                if (mode.ToString().ToUpper().Equals("CONTINUOUS"))
                {
                    data.AddRange(ByteConv.EncodeU16((ushort)offTime));
                }
                else
                {
                    // For timed mode, offTIme is always zero.
                    data.AddRange(ByteConv.EncodeU16((ushort)0x0000));
                }
            }
            else
            {
                data.Add((byte)0x00);
            }
            SendM5eCommand(data);
        }
        #endregion

        #endregion

        #region Alien Higgs2 Specific Commands

        #region CmdHiggs2PartialLoadImage

        /// <summary>
        /// Send the Alien Higgs2 Partial Load Image command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="epc">the EPC to write to the tag. Maximum of 12 bytes (96 bits)</param>
        /// <param name="target">target</param>
        private void CmdHiggs2PartialLoadImage(UInt16 timeout, UInt32 accessPassword, UInt32 killPassword, byte[] epc, TagFilter target)
        {
            if (null != target)
                throw new ReaderException("The method or operation is not supported.");
            List<byte> cmd = new List<byte>();
            msgAddHiggs2PartialLoadImage(ref cmd, timeout, accessPassword, killPassword, epc, target);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdHiggs2FullLoadImage

        /// <summary>
        /// Send the Alien Higgs2 Full Load Image command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="lockBits">the lock bits to write on the tag</param>
        /// <param name="pcWord">the PC word to write on the tag</param>
        /// <param name="epc">the EPC to write to the tag. Maximum of 12 bytes (96 bits)</param>
        /// <param name="target">target</param>
        private void CmdHiggs2FullLoadImage(UInt16 timeout, UInt32 accessPassword, UInt32 killPassword, UInt16 lockBits, UInt16 pcWord, byte[] epc, TagFilter target)
        {
            if (null != target)
                throw new ReaderException("The method or operation is not supported.");
            List<byte> cmd = new List<byte>();
            msgAddHiggs2FullLoadImage(ref cmd, timeout, accessPassword, killPassword, lockBits, pcWord, epc, target);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region msgAddHiggs2PartialLoadImage
        /// <summary>
        /// Higgs2PartialLoadImage
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="epc">the EPC to write to the tag. Maximum of 12 bytes (96 bits)</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddHiggs2PartialLoadImage(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, UInt32 killPassword, byte[] epc, TagFilter target)
        {
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x01);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            msg.Add(0x01);
            msg.AddRange(ByteConv.EncodeU32(killPassword));
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            msg.AddRange(epc);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddHiggs2PartialLoadImage

        #region msgAddHiggs2FullLoadImage
        /// <summary>
        /// Higgs2FullLoadImag
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="lockBits">the lock bits to write on the tag</param>
        /// <param name="pcWord">the PC word to write on the tag</param>
        /// <param name="epc">the EPC to write to the tag. Maximum of 12 bytes (96 bits)</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddHiggs2FullLoadImage(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, UInt32 killPassword, UInt16 lockBits, UInt16 pcWord, byte[] epc, TagFilter target)
        {
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x01);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            msg.Add(0x03);
            msg.AddRange(ByteConv.EncodeU32(killPassword));
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            msg.AddRange(ByteConv.EncodeU16(lockBits));
            msg.AddRange(ByteConv.EncodeU16(pcWord));
            msg.AddRange(epc);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddHiggs2FullLoadImage

        #region CmdHiggs3FastLoadImage

        /// <summary>
        /// Send the Alien Higgs3 Fast Load Image command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="currentAccessPassword">the access password to use to write to the tag</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="pcWord">the PC word to write on the tag</param>
        /// <param name="epc">the EPC to write to the tag. Must be exactly 12 bytes (96 bits)</param>
        /// <param name="target">target</param>
        private void CmdHiggs3FastLoadImage(UInt16 timeout, UInt32 currentAccessPassword, UInt32 accessPassword, UInt32 killPassword, UInt16 pcWord, byte[] epc, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddHiggs3FastLoadImage(ref cmd, timeout, currentAccessPassword, accessPassword, killPassword, pcWord, epc, target);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdHiggs3LoadImage

        /// <summary>
        /// Send the Alien Higgs3 Load Image command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="currentAccessPassword">the access password to use to write to the tag</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="pcWord">the PC word to write on the tag</param>
        /// <param name="epcAndUserData">
        /// the EPC and user data to write to the
        /// tag. Must be exactly 76 bytes. The pcWord specifies which of this
        /// is EPC and which is user data.
        /// </param>
        /// <param name="target">target</param>
        private void CmdHiggs3LoadImage(UInt16 timeout, UInt32 currentAccessPassword, UInt32 accessPassword, UInt32 killPassword, UInt16 pcWord, byte[] epcAndUserData, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddHiggs3LoadImage(ref cmd, timeout, currentAccessPassword, accessPassword, killPassword, pcWord, epcAndUserData, target);
            SendTimeout(cmd, timeout);
        }
        #endregion

        #region CmdHiggs3BlockReadLock

        /// <summary>
        /// Send the Alien Higgs3 Block Read Lock command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="currentAccessPassword">the access password to use to write to the tag</param>
        /// <param name="lockBits">a bitmask of bits to lock. Valid range 0-255</param>
        /// <param name="target">target</param>
        private void CmdHiggs3BlockReadLock(UInt16 timeout, UInt32 currentAccessPassword, byte lockBits, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddHiggs3BlockReadLock(ref cmd, timeout, currentAccessPassword, lockBits, target);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region msgAddHiggs3FastLoadImage
        /// <summary>
        /// msgAddHiggs3FastLoadImage
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="currentAccessPassword">the access password to use to write to the tag</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="pcWord">the PC word to write on the tag</param>
        /// <param name="epc">the EPC to write to the tag. Must be exactly 12 bytes (96 bits)</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddHiggs3FastLoadImage(ref List<byte> msg, UInt16 timeout, UInt32 currentAccessPassword, UInt32 accessPassword, UInt32 killPassword, UInt16 pcWord, byte[] epc, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x05);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x01);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                msg.Add(0x00);
                msg.Add(0x01);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            msg.AddRange(ByteConv.EncodeU32(currentAccessPassword));
            msg.AddRange(ByteConv.EncodeU32(killPassword));
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            msg.AddRange(ByteConv.EncodeU16(pcWord));
            msg.AddRange(epc);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddHiggs3FastLoadImage

        #region msgAddHiggs3LoadImage
        /// <summary>
        /// msgAddHiggs3LoadImage
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="currentAccessPassword">the access password to use to write to the tag</param>
        /// <param name="accessPassword">the access password to write on the tag</param>
        /// <param name="killPassword">the kill password to write on the tag</param>
        /// <param name="pcWord">the PC word to write on the tag</param>
        /// <param name="epcAndUserData">
        /// the EPC and user data to write to the
        /// tag. Must be exactly 76 bytes. The pcWord specifies which of this
        /// is EPC and which is user data.
        /// </param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddHiggs3LoadImage(ref List<byte> msg, UInt16 timeout, UInt32 currentAccessPassword, UInt32 accessPassword, UInt32 killPassword, UInt16 pcWord, byte[] epcAndUserData, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x05);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x03);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                msg.Add(0x00);
                msg.Add(0x03);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }

            msg.AddRange(ByteConv.EncodeU32(currentAccessPassword));
            msg.AddRange(ByteConv.EncodeU32(killPassword));
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            msg.AddRange(ByteConv.EncodeU16(pcWord));
            msg.AddRange(epcAndUserData);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddHiggs3LoadImage

        #region msgAddHiggs3BlockReadLock
        /// <summary>
        /// msgAddHiggs3BlockReadLock
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="currentAccessPassword">the access password to use to write to the tag</param>
        /// <param name="lockBits">a bitmask of bits to lock. Valid range 0-255</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddHiggs3BlockReadLock(ref List<byte> msg, UInt16 timeout, UInt32 currentAccessPassword, byte lockBits, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, currentAccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x05);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x09);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                msg.Add(0x00);
                msg.Add(0x09);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }

            msg.AddRange(ByteConv.EncodeU32(currentAccessPassword));
            msg.Add(lockBits);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddHiggs3BlockReadLock

        #endregion

        #region IDS Specific Commands

        /// <summary>
        /// Convert DateTime to SL900A time
        /// </summary>
        /// <param name="dt">DateTime object</param>
        /// <returns>32-bit SL900A time value</returns>
        public static UInt32 ToSL900aTime(DateTime dt)
        {
            UInt32 t32 = 0;
            t32 |= (UInt32)(dt.Year - 2010) << 26;
            t32 |= (UInt32)dt.Month << 22;
            t32 |= (UInt32)dt.Day << 17;
            t32 |= (UInt32)dt.Hour << 12;
            t32 |= (UInt32)dt.Minute << 6;
            t32 |= (UInt32)dt.Second << 0;
            return t32;
        }
        /// <summary>
        /// Convert SL900A time to DateTime
        /// </summary>
        /// <param name="t32">32-bit SL900A time value</param>
        /// <returns>DateTime object</returns>
        public static DateTime FromSL900aTime(UInt32 t32)
        {
            return new DateTime(
                2010 + (int)((t32 >> 26) & 0x3F), //year
                (int)((t32 >> 22) & 0xF), //month
                (int)((t32 >> 17) & 0x1F), //day
                (int)((t32 >> 12) & 0x1F), //hour
                (int)((t32 >> 6) & 0x3F), //minute
                (int)((t32 >> 0) & 0x3F) //second
                );
        }

        private Object CmdIdsSL900aAccessFifo(UInt16 timeout, Gen2.IDS.SL900A.AccessFifo tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aAccessFifo(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            if (tagop is Gen2.IDS.SL900A.AccessFifoRead)
            {
                byte[] respdata = GetDataFromM5eResponse(response);
                byte[] val = SubArray(respdata, 4, respdata.Length - 4);
                return val;
            }
            else if (tagop is Gen2.IDS.SL900A.AccessFifoStatus)
            {
                byte val = GetDataFromM5eResponse(response)[4];
                return new Gen2.IDS.SL900A.FifoStatus(val);
            }
            else if (tagop is Gen2.IDS.SL900A.AccessFifoWrite)
            {
                return null;
            }
            else
            {
                throw new ArgumentException("Unsupported AccessFifo tagop: " + tagop);
            }
        }
        private void CmdIdsSL900aEndLog(UInt16 timeout, Gen2.IDS.SL900A.EndLog tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aEndLog(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private Gen2.IDS.SL900A.CalSfe CmdIdsSL900aGetCalibrationData(UInt16 timeout, Gen2.IDS.SL900A.GetCalibrationData
    tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aGetCalibrationData(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            byte[] data = GetDataFromM5eResponse(response);
            return new Gen2.IDS.SL900A.CalSfe(data, 4);
        }
        private Gen2.IDS.SL900A.LogState CmdIdsSL900aGetLogState(UInt16 timeout, Gen2.IDS.SL900A.GetLogState
    tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aGetLogState(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            byte[] data = GetDataFromM5eResponse(response);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] reply = SubArray(data, 4, (data.Length + 5 - i));
            return new Gen2.IDS.SL900A.LogState(reply);
        }
        private Gen2.IDS.SL900A.SensorReading CmdIdsSL900aGetSensorValue(UInt16 timeout, Gen2.IDS.SL900A.GetSensorValue tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aGetSensorValue(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int offset = 0;
            if (enableMultipleSelect)
            {
                offset = 5;
            }
            else
            {
                offset = 4;
            }
            UInt16 sensorReply = ByteConv.ToU16(GetDataFromM5eResponse(response), offset);
            return new Gen2.IDS.SL900A.SensorReading(sensorReply);
        }
        private void CmdIdsSL900aInitialize(UInt16 timeout, Gen2.IDS.SL900A.Initialize tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aInitialize(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private void CmdIdsSL900aSetCalibrationData(UInt16 timeout, Gen2.IDS.SL900A.SetCalibrationData tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aSetCalibrationData(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private void CmdIdsSL900aSetLogMode(UInt16 timeout, Gen2.IDS.SL900A.SetLogMode tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aSetLogMode(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private void CmdIdsSL900aSetSfeParameters(UInt16 timeout, Gen2.IDS.SL900A.SetSfeParameters tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aSetSfeParameters(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private void CmdIdsSL900aStartLog(UInt16 timeout, Gen2.IDS.SL900A.StartLog tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aStartLog(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private Gen2.IDS.SL900A.BatteryLevelReading CmdIdsSL900aGetBatteryLevel(UInt16 timeout, Gen2.IDS.SL900A.GetBatteryLevel tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aGetBatteryLevel(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            UInt16 batteryLevelReply = ByteConv.ToU16(GetDataFromM5eResponse(response), 4);
            return new Gen2.IDS.SL900A.BatteryLevelReading(batteryLevelReply);
        }
        private Gen2.IDS.SL900A.MeasurementSetupData CmdIdsSL900aGetMeasurementSetup(UInt16 timeout, Gen2.IDS.SL900A.GetMeasurementSetup tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            List<byte> resp = new List<byte>();
            msgAddIdsSL900aGetMeasurementSetupValue(ref cmd, timeout, tagop, filter);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            int i = enableMultipleSelect ? 10 : 9;
            byte[] measurementSetupData = SubArray(response, 4, (response.Length + 5 - i));
            return new Gen2.IDS.SL900A.MeasurementSetupData(measurementSetupData, 0);
        }
        private void cmdIdsSL900aSetPassword(UInt16 timeout, Gen2.IDS.SL900A.SetPassword tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aSetPassword(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private void cmdIdsSL900aSetLogLimit(UInt16 timeout, Gen2.IDS.SL900A.SetLogLimit tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aSetLogLimit(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }
        private void cmdIdsSL900aSetShelfLife(UInt16 timeout, Gen2.IDS.SL900A.SetShelfLife tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIdsSL900aSetShelfLife(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
        }

        private byte[] CmdIAVDenatranCustomTagOp(UInt16 timeout, Gen2.Denatran.IAV tagop, UInt32 password, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIAVDenatran(ref cmd, timeout, tagop, password, filter);
            byte[] responseIAVDenatran = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            byte[] response = new byte[responseIAVDenatran.Length - 4];
            //Extracting only the data bytes and excluding command type, ex: 00 00 - Active secure mode and 00 01 - authenticate OBU
            Array.Copy(responseIAVDenatran, 4, response, 0, responseIAVDenatran.Length - 4);
            return response;
        }

        private byte msgAddIAVDenatran(ref List<byte> msg, UInt16 timeout, Gen2.Denatran.IAV tagop, UInt32 password, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, password);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16((ushort)tagop.Mode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (null != target)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.AddRange(ByteConv.EncodeU16((ushort)tagop.Mode));
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            // Add payload for all denatran iav tag operations except GetTokenId tag operation
            if (!(tagop is Gen2.Denatran.IAV.GetTokenId))
            {
                msg.Add(tagop.Payload);
            }
            if (tagop is Gen2.Denatran.IAV.ActivateSiniavMode)
            {
                // Add the token field
                if (null != ((Gen2.Denatran.IAV.ActivateSiniavMode)tagop).Token)
                {
                    msg.AddRange(((Gen2.Denatran.IAV.ActivateSiniavMode)tagop).Token);
                }
            }
            else if ((tagop is Gen2.Denatran.IAV.OBUReadFromMemMap) || tagop is Gen2.Denatran.IAV.ReadSec)
            {
                // Pointer to the user data
                msg.AddRange(ByteConv.EncodeU16(tagop.WritePtr));
            }
            else if (tagop is Gen2.Denatran.IAV.OBUWriteToMemMap)
            {
                // Pointer to the user data
                msg.AddRange(ByteConv.EncodeU16(tagop.WritePtr));
                msg.AddRange(ByteConv.EncodeU16(tagop.WordData));
                msg.AddRange(tagop.WriteCredentials.TagId);
                msg.AddRange(tagop.WriteCredentials.DataBuf);
            }
            else if (tagop is Gen2.Denatran.IAV.WriteSec)
            {
                // Pointer to the user data
                msg.AddRange(tagop.WriteSecCredentials.Data);
                msg.AddRange(tagop.WriteSecCredentials.Credentials);
            }
            return (byte)(msg.Count - tmp);
        }

        private byte msgAddIdsSL900aSetShelfLife(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.SetShelfLife tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.AddRange(ByteConv.EncodeU32(tagop.shelfLifeBlock0.Raw));
            msg.AddRange(ByteConv.EncodeU32(tagop.shelfLifeBlock1.Raw));
            return (byte)(msg.Count - tmp);
        }

        private int msgAddIdsSL900aCommonHeader(List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(tagop.CommandCode);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.Add(0x00);
                    msg.Add(tagop.CommandCode);
                }
                else
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                    msg.Add(0x00);
                    msg.Add(tagop.CommandCode);
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }

            msg.Add((byte)tagop.PasswordLevel);
            msg.AddRange(ByteConv.EncodeU32(tagop.Password));
            return tmp;
        }
        private byte msgAddIdsSL900aAccessFifo(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.AccessFifo tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);

            byte length = 0;
            byte[] payload = null;
            if (tagop is Gen2.IDS.SL900A.AccessFifoRead)
            {
                Gen2.IDS.SL900A.AccessFifoRead op = (Gen2.IDS.SL900A.AccessFifoRead)tagop;
                length = op.Length;
            }
            else if (tagop is Gen2.IDS.SL900A.AccessFifoWrite)
            {
                Gen2.IDS.SL900A.AccessFifoWrite op = (Gen2.IDS.SL900A.AccessFifoWrite)tagop;
                length = (byte)op.Payload.Length;
                payload = op.Payload;
            }

            msg.Add((byte)((byte)tagop.Subcommand | length));
            if (null != payload) { msg.AddRange(payload); }

            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aEndLog(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.EndLog tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aGetCalibrationData(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.GetCalibrationData tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aGetLogState(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.GetLogState tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aGetSensorValue(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.GetSensorValue tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.Add((byte)tagop.SensorType);
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aGetBatteryLevel(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.GetBatteryLevel tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.Add((byte)tagop.batteryType);
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aInitialize(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.Initialize tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.AddRange(ByteConv.EncodeU16(tagop.DelayTime.Raw));
            msg.AddRange(ByteConv.EncodeU16(tagop.AppData.Raw));
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aSetCalibrationData(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.SetCalibrationData tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            byte[] calbytes = new byte[8];
            ByteConv.FromU64(calbytes, 0, tagop.Cal.Raw);
            msg.AddRange(SubArray(calbytes, 1, 7));
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aSetLogMode(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.SetLogMode tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            UInt32 logmode = 0;
            logmode |= (UInt32)tagop.Form << 21;
            logmode |= (UInt32)tagop.Storage << 20;
            logmode |= (UInt32)(tagop.Ext1Enable ? 1 : 0) << 19;
            logmode |= (UInt32)(tagop.Ext2Enable ? 1 : 0) << 18;
            logmode |= (UInt32)(tagop.TempEnable ? 1 : 0) << 17;
            logmode |= (UInt32)(tagop.BattEnable ? 1 : 0) << 16;
            logmode |= (UInt32)tagop.LogInterval << 1;
            msg.Add((byte)((logmode >> 16) & 0xFF));
            msg.Add((byte)((logmode >> 8) & 0xFF));
            msg.Add((byte)((logmode >> 0) & 0xFF));
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aSetSfeParameters(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.SetSfeParameters tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.AddRange(ByteConv.EncodeU16(tagop.Sfe.Raw));
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aStartLog(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.StartLog tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.AddRange(ByteConv.EncodeU32(ToSL900aTime(tagop.StartTime)));
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aGetMeasurementSetupValue(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.GetMeasurementSetup tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aSetPassword(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.SetPassword tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            msg.Add((byte)tagop.newPasswordLevel);
            msg.AddRange(ByteConv.EncodeU32(tagop.newPassword));
            return (byte)(msg.Count - tmp);
        }
        private byte msgAddIdsSL900aSetLogLimit(ref List<byte> msg, UInt16 timeout, Gen2.IDS.SL900A.SetLogLimit tagop, TagFilter target)
        {
            int tmp = msgAddIdsSL900aCommonHeader(msg, timeout, tagop, target);
            byte[] logLimitbytes = new byte[8];
            ByteConv.FromU64(logLimitbytes, 0, tagop.LogLimits.Raw);
            msg.AddRange(SubArray(logLimitbytes, 3, 5));
            return (byte)(msg.Count - tmp);
        }

        #endregion

        #region NXP Specific Commands

        #region CmdNxpSetReadProtect

        /// <summary>
        /// Send the NXP Set Read Protect command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        private void CmdNxpSetReadProtect(UInt16 timeout, UInt32 accessPassword, byte chipType, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddNxpSetReadProtect(ref cmd, timeout, accessPassword, chipType, target);
            SendTimeout(cmd, timeout);
        }
        #endregion

        #region CmdNxpResetReadProtect

        /// <summary>
        /// Send the NXP Reset Read Protect command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        private void CmdNxpResetReadProtect(UInt16 timeout, UInt32 accessPassword, byte chipType, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddNxpResetReadProtect(ref cmd, timeout, accessPassword, chipType, target);
            SendTimeout(cmd, timeout);
        }
        #endregion

        #region CmdNxpChangeEas

        /// <summary>
        /// Send the NXP Change EAS command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="reset">true to reset the EAS, false to set it</param>    
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        private void CmdNxpChangeEas(UInt16 timeout, UInt32 accessPassword, bool reset, byte chipType, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddNxpChangeEas(ref cmd, timeout, accessPassword, reset, chipType, target);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdNxpEasAlarm
        /// <summary>
        /// Send the NXP EAS Alarm command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="dr">Gen2 divide ratio to use</param>
        /// <param name="m">Gen2 M parameter to use</param>
        /// <param name="trExt">Gen2 TrExt value to use</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns>8 bytes of EAS alarm data</returns>
        private byte[] CmdNxpEasAlarm(UInt16 timeout, Gen2.DivideRatio dr, Gen2.TagEncoding m, Gen2.TrExt trExt, byte chipType, TagFilter target)
        {
            if (null != target)
                throw new ReaderException("The method or operation is not implemented.");
            List<byte> cmd = new List<byte>();
            msgAddNxpEasAlarm(ref cmd, timeout, dr, m, trExt, chipType, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            byte[] responseToSend = new byte[response.Length - 1];
            Array.Copy(response, 1, responseToSend, 0, response.Length - 1);
            return responseToSend;
        }
        #endregion

        #region CmdNxpCalibrate

        /// <summary>
        /// Send the NXP Calibrate command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="target">target</param>
        /// <returns>64 bytes of calibration data</returns>
        private byte[] CmdNxpCalibrate(UInt16 timeout, UInt32 accessPassword, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddNxpCalibrate(ref cmd, timeout, accessPassword, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            byte[] returnResponse = new byte[response.Length - 1];
            Array.Copy(response, 1, returnResponse, 0, response.Length - 1);
            return returnResponse;
        }
        #endregion

        #region cmdNxpChangeConfig
        /// <summary>
        /// NXP ChangeConfig (only for G2iL)
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="configData">configWord (I/O)The config word to write on the tag</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns></returns>
        private Gen2.NXP.G2I.ConfigWord cmdNxpChangeConfig(UInt16 timeout, UInt32 accessPassword, UInt16 configData, byte chipType, TagFilter target)
        {
            if (0x07 != chipType)
                throw new ReaderException("The method or operation is not supported for this tag." + chipType.ToString());
            List<byte> cmd = new List<byte>();
            msgAddNxpChangeConfig(ref cmd, timeout, accessPassword, configData, chipType, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            Gen2.NXP.G2I.ConfigWord word = new Gen2.NXP.G2I.ConfigWord();
            if (isMultiFilterEnabled)
            {
                return word.GetConfigWord(ByteConv.ToU16(response, 5));
            }
            else
            {
                return word.GetConfigWord(ByteConv.ToU16(response, 4));
            }
        }
        #endregion

        #region cmdNxpUCODe7ChangeConfig
        /// <summary>
        /// NXPUCODE7 ChangeConfig 
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="configData">configWord (I/O)The config word to write on the tag</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns></returns>
        private void cmdNxpUCODE7ChangeConfig(UInt16 timeout, UInt32 accessPassword, UInt16 configData, byte chipType, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddNxpUCODE7ChangeConfig(ref cmd, timeout, accessPassword, configData, chipType, target);
            GetDataFromM5eResponse(SendTimeout(cmd, timeout));
        }
        #endregion

        #region cmdGen2V2NxpUntraceable

        /// <summary>
        /// Send the NXP Gen2v2 Untraceable command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="Untraceable">Untraceable options</param>
        /// <param name="target">target</param>
        private void cmdGen2V2NxpUntraceable(ushort timeout, uint accessPassword, Gen2.NXP.AES.Untraceable Untraceable, TagFilter target)
        {
            //if (null != target)
            //    throw new ReaderException("The method or operation is not implemented.");
            List<byte> cmd = new List<byte>();
            msgAddGen2v2NxpUntraceable(ref cmd, timeout, accessPassword, Untraceable, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
        }

        #endregion

        #region cmdGen2V2NxpAuthenticate
        /// <summary>
        /// Send the NXP Gen2v2 Authenticate command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="Authenticate">Authenticate options</param>
        /// <param name="target">target</param>
        /// <returns>byte array response</returns>
        private byte[] cmdGen2V2NxpAuthenticate(ushort timeout, uint accessPassword, Gen2.NXP.AES.Authenticate Authenticate, TagFilter target)
        {
            //if (null != target)
            //    throw new ReaderException("The method or operation is not implemented.");
            List<byte> cmd = new List<byte>();
            msgAddGen2v2NxpAuthenticate(ref cmd, timeout, accessPassword, Authenticate, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            int ptr = 3;
            if (enableMultipleSelect)
            {
                ptr = 4;
            }
            int datalength = response.Length - ptr;
            return SubArray(response, ptr, datalength);
        }

        #endregion

        #region cmdGen2V2NxpReadBuffer
        /// <summary>
        /// Send the NXP Gen2v2 Authenticate command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="ReadBuffer">ReadBuffer options</param>
        /// <param name="target">target</param>
        /// <returns>byte array response</returns>
        private byte[] cmdGen2V2NxpReadBuffer(ushort timeout, uint accessPassword, Gen2.NXP.AES.ReadBuffer ReadBuffer, TagFilter target)
        {
            //if (null != target)
            //    throw new ReaderException("The method or operation is not implemented.");
            List<byte> cmd = new List<byte>();
            msgAddGen2v2NxpReadBuffer(ref cmd, timeout, accessPassword, ReadBuffer, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            int ptr = 3;
            if (enableMultipleSelect)
            {
                ptr = 4;
            }
            int datalength = response.Length - ptr;
            return SubArray(response, ptr, datalength);
        }

        #endregion

        #region msgAddGen2v2NxpReadBuffer
        /// <summary>
        /// msgAddGen2v2NxpReadBuffer
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="ReadBuffer">ReadBuffer object</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddGen2v2NxpReadBuffer(ref List<byte> msg, ushort timeout, uint accessPassword, Gen2.NXP.AES.ReadBuffer ReadBuffer, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(ReadBuffer.aes.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x00 | sb.Option)); // option flag
                msg.Add(ReadBuffer.SubCommand);
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)0x00);
                    msg.Add(ReadBuffer.SubCommand);
                }
                else
                {
                    msg.Add((byte)((0x00) | (byte)(sbNewArray[0]))); // option flag
                    msg.Add(ReadBuffer.SubCommand);
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }

            msg.AddRange(ByteConv.EncodeU16(ReadBuffer.GetCmdWordPointer()));
            msg.AddRange(ByteConv.EncodeU16(ReadBuffer.GetCmdBitCount()));
            Gen2.NXP.AES.Tam1Authentication tam;
            tam = ReadBuffer.tam1 != null ? ReadBuffer.tam1 : ReadBuffer.tam2;
            msg.Add(tam.Authentication);
            msg.Add(tam.CSI); // CSI
            msg.Add(tam.keyID); // Key
            msg.Add(tam.KeyLength); //keyLength
            msg.AddRange(ByteConv.ConvertFromUshortArray(tam.Key));
            if (tam is Gen2.NXP.AES.Tam2Authentication)
            {
                msg.AddRange(ByteConv.EncodeU16(ReadBuffer.tam2.GetCmdProfileOffset()));
                msg.Add(ReadBuffer.tam2.GetCmdProtModeBlockCount());
            }
            return (byte)(msg.Count - tmp);
        }

        #endregion

        #region msgAddGen2v2NxpAuthenticate
        /// <summary>
        /// msgAddGen2v2NxpAuthenticate
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="Authenticate">Authenticate object</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddGen2v2NxpAuthenticate(ref List<byte> msg, ushort timeout, uint accessPassword, Gen2.NXP.AES.Authenticate Authenticate, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(Authenticate.aes.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x00 | sb.Option)); // option flag
                msg.Add(Authenticate.SubCommand);
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)0x00);
                    msg.Add(Authenticate.SubCommand);
                }
                else
                {
                    msg.Add((byte)((0x00) | (byte)(sbNewArray[0]))); // option flag
                    msg.Add(Authenticate.SubCommand);
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            Gen2.NXP.AES.Tam1Authentication tam;
            tam = Authenticate.tam1 != null ? Authenticate.tam1 : Authenticate.tam2;
            msg.Add(tam.Authentication);
            msg.Add(tam.CSI); // CSI
            msg.Add(tam.keyID); // Key
            msg.Add(tam.KeyLength); //keyLength
            msg.AddRange(ByteConv.ConvertFromUshortArray(tam.Key));
            if (tam is Gen2.NXP.AES.Tam2Authentication)
            {
                msg.AddRange(ByteConv.EncodeU16(Authenticate.tam2.GetCmdProfileOffset()));
                msg.Add(Authenticate.tam2.GetCmdProtModeBlockCount());
            }
            return (byte)(msg.Count - tmp);
        }

        #endregion

        #region msgAddGen2v2NxpUntraceable

        /// <summary>
        /// msgAddGen2v2NxpUntraceable
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword"></param>
        /// <param name="Untraceable">UntUntraceable options</param>
        /// <param name="target"></param>
        private byte msgAddGen2v2NxpUntraceable(ref List<byte> msg, ushort timeout, uint accessPassword, Gen2.NXP.AES.Untraceable Untraceable, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(Untraceable.aes.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x00 | sb.Option)); // option flag
                msg.Add(Untraceable.SubCommand);
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)0x00);
                    msg.Add(Untraceable.SubCommand);
                }
                else
                {
                    msg.Add((byte)((0x00) | (byte)(sbNewArray[0]))); // option flag
                    msg.Add(Untraceable.SubCommand);
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }

            }
            msg.AddRange(ByteConv.EncodeU16(Untraceable.GetConfigWord()));
            if (Untraceable.SubCommand == 0x02)
            {
                msg.Add(Untraceable.auth.Authentication);
                msg.Add(Untraceable.auth.CSI); // CSI
                msg.Add(Untraceable.auth.keyID); // Key
                msg.Add(Untraceable.auth.KeyLength); //keyLength
                msg.AddRange(ByteConv.ConvertFromUshortArray(Untraceable.auth.Key));
            }
            else
            {
                msg.AddRange(ByteConv.EncodeU32(Untraceable.aes.AccessPassword));
            }
            return (byte)(msg.Count - tmp);
        }

        #endregion

        #region msgAddNxpSetReadProtect
        /// <summary>
        /// msgAddNxpSetReadProtect
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddNxpSetReadProtect(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, byte chipType, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(chipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x01);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (null != target)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add(0x01);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddNxpSetReadProtect

        #region msgAddNxpResetReadProtect
        /// <summary>
        /// msgAddNxpResetReadProtect
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddNxpResetReadProtect(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, byte chipType, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(chipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x02);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (null != target)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add(0x02);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddNxpResetReadProtect

        #region msgAddNxpChangeEas
        /// <summary>
        /// msgAddNxpChangeEas
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="reset">true to reset the EAS, false to set it</param> 
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddNxpChangeEas(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, bool reset, byte chipType, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(chipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x03);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (null != target)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add(0x03);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            byte setReset = 0;
            if (reset)
            {
                setReset = 0x02;
            }
            else
            {
                setReset = 0x01;
            }
            msg.Add(setReset);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddNxpChangeEas

        #region msgAddNxpEasAlarm
        /// <summary>
        /// msgAddNxpEasAlarm
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="dr">Gen2 divide ratio to use</param>
        /// <param name="m">Gen2 M parameter to use</param>
        /// <param name="trExt">Gen2 TrExt value to use</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddNxpEasAlarm(ref List<byte> msg, UInt16 timeout, Gen2.DivideRatio dr, Gen2.TagEncoding m, Gen2.TrExt trExt, byte chipType, TagFilter target)
        {
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add((byte)chipType);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (0x07 == chipType)
            {
                msg.Add(0x40);
                msg.Add(0x00);
            }
            msg.Add(0x04);
            msg.Add((byte)dr);
            msg.Add((byte)m);
            msg.Add((byte)trExt);
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddNxpEasAlarm

        /// <summary>
        /// msgAddNxpChangeConfig
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="configData"> configuration data</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>        
        private byte msgAddNxpChangeConfig(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, UInt16 configData, byte chipType, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(chipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x07);
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (target != null)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add(0x07);
                if (target != null)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }

            }
            msg.Add(0x00); //RFU
            msg.AddRange(ByteConv.EncodeU16(configData));
            return (byte)(msg.Count - tmp);
        }

        #region msgAddNxpUCODE7ChangeConfig
        /// <summary>
        /// msgAddNxpUCODE7ChangeConfig
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="configData"> configuration data</param>
        /// <param name="chipType">NXP chip type</param>
        /// <param name="target">target</param>        
        private byte msgAddNxpUCODE7ChangeConfig(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, UInt16 configData, byte chipType, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(chipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add((byte)Gen2.NXP.UCODE7.UCODE7SubCmd.CHANGE_CONFIG_COMMAND);
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (target != null)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add((byte)Gen2.NXP.UCODE7.UCODE7SubCmd.CHANGE_CONFIG_COMMAND);
                if (target != null)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }

            msg.AddRange(ByteConv.EncodeU16(configData));
            return (byte)(msg.Count - tmp);
        }
        #endregion

        #region msgAddNxpCalibrate
        /// <summary>
        /// msgAddNxpCalibrate
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="target">target</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddNxpCalibrate(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, false, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x02);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x05);
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (null != target)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add(0x05);
                if (null != target)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            msg.AddRange(ByteConv.EncodeU32(accessPassword));
            return (byte)(msg.Count - tmp);
        }

        #endregion msgAddNxpCalibrate

        #endregion

        #region Hibiki Specific Tags

        #region CmdHibikiReadLock

        /// <summary>
        /// Send the Hitachi Hibiki Read Lock command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="mask">bitmask of read lock bits to alter</param>
        /// <param name="action">action value of read lock bits to alter</param>
        [Obsolete()]
        public void CmdHibikiReadLock(UInt16 timeout, UInt32 accessPassword, byte mask, byte action)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x2D);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x06);
            cmd.Add(0x00);
            cmd.AddRange(ByteConv.EncodeU32(accessPassword));
            cmd.Add(mask);
            cmd.Add(action);
            SendTimeout(cmd, timeout);
        }
        #endregion

        #region CmdHibikiGetSystemInformation

        /// <summary>
        /// Send the Hitachi Hibiki Get System Information command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <returns>
        /// 10-element array of integers: {info flags, reserved memory size,
        /// EPC memory size, TID memory size, user memory size, set attenuate value,
        /// bank lock bits, block read lock bits, block r/w lock bits, block write
        /// lock bits}
        /// </returns>
        [Obsolete()]
        public HibikiSystemInformation CmdHibikiGetSystemInformation(UInt16 timeout, UInt32 accessPassword)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x2D);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x06);
            cmd.Add(0x01);
            cmd.AddRange(ByteConv.EncodeU32(accessPassword));
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            HibikiSystemInformation info = new HibikiSystemInformation();
            int ptr = 1;
            info.infoFlags = ByteConv.ToU16(response, ptr++);
            info.reservedMemory = response[ptr++];
            info.epcMemory = response[ptr++];
            info.tidMemory = response[ptr++];
            info.userMemory = response[ptr++];
            info.setAttenuate = response[ptr++];
            info.bankLock = ByteConv.ToU16(response, ptr);
            ptr += 2;
            info.blockReadLock = ByteConv.ToU16(response, ptr);
            ptr += 2;
            info.blockRwLock = ByteConv.ToU16(response, ptr);
            ptr += 2;
            info.blockWriteLock = ByteConv.ToU16(response, ptr);
            return info;
        }
        #endregion

        #region CmdHibikiSetAttenuate

        /// <summary>
        /// Send the Hitachi Hibiki Set Attenuate command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="level">the attenuation level to set</param>
        /// <param name="_lock">whether to permanently lock the attenuation level</param>
        [Obsolete()]
        public void CmdHibikiSetAttenuate(UInt16 timeout, UInt32 accessPassword, byte level, byte _lock)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x2D);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x06);
            cmd.Add(0x04);
            cmd.AddRange(ByteConv.EncodeU32(accessPassword));
            cmd.Add(level);
            cmd.Add(_lock);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdHibikiBlockLock

        /// <summary>
        /// Send the Hitachi Hibiki Block Lock command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="block">the block of memory to operate on</param>
        /// <param name="blockPassword">the password for the block</param>
        /// <param name="mask">bitmask of lock bits to alter</param>
        /// <param name="action">value of lock bits to alter</param>
        [Obsolete()]
        public void CmdHibikiBlockLock(UInt16 timeout, UInt32 accessPassword, byte block, UInt32 blockPassword, byte mask, byte action)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x2D);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x06);
            cmd.Add(0x05);
            cmd.AddRange(ByteConv.EncodeU32(accessPassword));
            cmd.Add(block);
            cmd.AddRange(ByteConv.EncodeU32(blockPassword));
            cmd.Add(mask);
            cmd.Add(action);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdHibikiBlockReadLock

        /// <summary>
        /// Send the Hitachi Hibiki Block Read Lock command.
        /// </summary>
        /// <param name="timeout">Timeout to Block Read Lock</param>
        /// <param name="accessPassword">Access Password</param>
        /// <param name="block">Block</param>
        /// <param name="blockPassword">Block Access Password</param>
        /// <param name="mask">Mask</param>
        /// <param name="action">Action</param>
        [Obsolete()]
        public void CmdHibikiBlockReadLock(UInt16 timeout, UInt32 accessPassword, byte block, UInt32 blockPassword, byte mask, byte action)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x2D);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x06);
            cmd.Add(0x06);
            cmd.AddRange(ByteConv.EncodeU32(accessPassword));
            cmd.Add(block);
            cmd.AddRange(ByteConv.EncodeU32(blockPassword));
            cmd.Add(mask);
            cmd.Add(action);
            SendTimeout(cmd, timeout);
        }

        #endregion

        #region CmdHibikiWriteMultipleWords

        /// <summary>
        /// Send the Hitachi Hibiki Write Multiple Words Lock command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="bank">the Gen2 memory bank to write to</param>
        /// <param name="wordOffset">the word address to start writing at</param>
        /// <param name="data">the data to write - must be an even number of bytes</param>
        [Obsolete()]
        public void CmdHibikiWriteMultipleWords(UInt16 timeout, UInt32 accessPassword, Gen2.Bank bank, UInt32 wordOffset, ICollection<byte> data)
        {
            List<byte> cmd = new List<byte>();
            cmd.Add(0x2D);
            cmd.AddRange(ByteConv.EncodeU16(timeout));
            cmd.Add(0x06);
            cmd.Add(0x07);
            cmd.AddRange(ByteConv.EncodeU32(accessPassword));
            cmd.Add((byte)bank);
            cmd.AddRange(ByteConv.EncodeU32(wordOffset));
            cmd.Add((byte)data.Count);
            cmd.AddRange(data);
            SendTimeout(cmd, timeout);
        }
        #endregion

        #endregion

        #region ImpinjMonza4


        /// <summary>
        /// Send the Impinj Monza4 QTReadWrite command.
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="controlByte">Monza4 QT Control Byte</param>
        /// <param name="payLoad">Monza4 Payload word</param>
        /// <param name="target">filter</param>
        /// <returns>Gen2.QTPayload</returns>
        private Gen2.Impinj.Monza4.QTPayload CmdMonza4QTReadWrite(UInt16 timeout, UInt32 accessPassword, int controlByte, int payLoad, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddMonza4QTReadWrite(ref cmd, timeout, accessPassword, controlByte, payLoad, target);
            byte[] response = GetDataFromM5eResponse(SendTimeout(cmd, timeout));
            byte[] responseToSend = new byte[2];
            //extract payload
            Array.Copy(response, 4, responseToSend, 0, 2);

            Gen2.Impinj.Monza4.QTPayload qtPayload = new Gen2.Impinj.Monza4.QTPayload();
            //convert byte[] to int
            int res = ByteConv.ToU16(responseToSend, 0);
            //Construct QTPayload
            if ((res & 0x8000) != 0)
            {
                qtPayload.QTSR = true;
            }
            if ((res & 0x4000) != 0)
            {
                qtPayload.QTMEM = true;
            }
            return qtPayload;

        }

        /// <summary>
        /// Form the message QTReadWrite command.
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="controlByte">Monza4 QT Control Byte</param>
        /// <param name="payLoad">Monza4 Payload word</param>
        /// <param name="target">filter</param>
        /// <returns>the length of the assembled embedded command</returns>
        private byte msgAddMonza4QTReadWrite(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, int controlByte, int payLoad, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(0x08);//chip type
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.Add(0x00);
                msg.Add(0x00);
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (target != null)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.Add(0x00);
                msg.Add(0x00);
                if (target != null)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            //control byte
            msg.Add((byte)controlByte);
            //payload
            msg.AddRange(ByteConv.EncodeU16((UInt16)payLoad));

            return (byte)(msg.Count - tmp);
        }

        #endregion ImpinjMonza4

        #region ImpinjMonza6

        #region cmdMonza6MarginRead
        /// <summary>
        /// Impinj Monza6 MarginRead
        /// </summary>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="bank"> gen2 memory bank to read from </param>
        /// <param name="bitAddress"> bitAddress to start reading from </param>
        /// <param name="maskBitlength"> the mask bit length </param>
        /// <param name="mask"> the mask bits </param>
        /// <param name="chipType">chipType of the tag</param>
        /// <param name="target">target filter</param> 

        private void cmdMonza6MarginRead(UInt16 timeout, UInt32 accessPassword, Gen2.Bank bank, UInt32 bitAddress, byte maskBitlength, byte[] mask, byte chipType, TagFilter target)
        {
            List<byte> cmd = new List<byte>();
            msgAddMonza6MarginRead(ref cmd, timeout, accessPassword, bank, bitAddress, maskBitlength, mask, chipType, target);
            GetDataFromM5eResponse(SendTimeout(cmd, timeout));
        }
        #endregion cmdMonza6MarginRead

        #region msgAddMonza6MarginRead
        /// <summary>
        /// msgAddMonza6MarginRead
        /// </summary>
        /// <param name="msg">The embedded command bytes</param>
        /// <param name="timeout">the timeout of the operation, in milliseconds. Valid range is 0-65535.</param>
        /// <param name="accessPassword">the access password to use to write to the tag</param>
        /// <param name="bank"> gen2 memory bank to read from </param>
        /// <param name="bitAddress"> bitAddress to start reading from </param>
        /// <param name="maskBitlength"> the mask bit length </param>
        /// <param name="mask"> the mask bits </param>
        /// <param name="chipType">chipType of the tag</param>
        /// <param name="target">target filter</param>        
        private byte msgAddMonza6MarginRead(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, Gen2.Bank bank, UInt32 bitAddress, byte maskBitlength, byte[] mask, byte chipType, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(chipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                //msg.Add(0x00);
                msg.AddRange(ByteConv.EncodeU16((ushort)Gen2.Impinj.Monza6.Monza6SubCmd.MARGIN_READ));
                msg.AddRange(sb.Mask);
            }
            else
            {
                if (target != null)
                {
                    msg.Add((byte)(0x40 | (byte)(sbNewArray[0])));
                }
                else
                {
                    msg.Add(0x40);
                }
                msg.AddRange(ByteConv.EncodeU16((ushort)Gen2.Impinj.Monza6.Monza6SubCmd.MARGIN_READ));
                if (target != null)
                {
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    msg.AddRange(sbModified);
                }
            }
            msg.Add((byte)bank);
            msg.AddRange(ByteConv.EncodeU32(bitAddress));
            msg.Add(maskBitlength);
            msg.AddRange(Truncate(mask, ByteConv.BytesPerBits(maskBitlength)));

            return (byte)(msg.Count - tmp);
        }
        #endregion msgAddMonza6MarginRead

        #endregion ImpinjMonza6

        #region FUDAN
        //Fudan Read Reg
        private byte[] Cmd_init_FudanReadReg(UInt16 timeout, Gen2.Fudan.Fudan_ReadReg tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanReadReg(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            //byte[] responseToSend = new byte[response.Length - 10];
            //Array.Copy(response, 10, responseToSend, 0, response.Length - 10);
            //extract response
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanReadReg(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_ReadReg tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41 ));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(ByteConv.EncodeU16(tagop.StartAddress));
            return (byte)(msg.Count - tmp);
        }

        //Fudan Write Reg
        private byte[] Cmd_init_FudanWriteReg(UInt16 timeout, Gen2.Fudan.Fudan_WriteReg tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanWriteReg(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        //msgAddFudanWriteReg
        private byte msgAddFudanWriteReg(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_WriteReg tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(ByteConv.EncodeU16(tagop.StartAddress));
            msg.AddRange(ByteConv.EncodeU16(tagop.WriteData));
            return (byte)(msg.Count - tmp);
        }

        //Fudan Load Reg
        private byte[] Cmd_init_FudanLoadReg(UInt16 timeout, Gen2.Fudan.Fudan_LoadReg tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanLoadReg(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanLoadReg(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_LoadReg tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(tagop.CmdConfig);
            return (byte)(msg.Count - tmp);
        }

        //Fudan Read Mem
        private byte[] Cmd_init_FudanReadMem(UInt16 timeout, Gen2.Fudan.Fudan_ReadMem tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanReadMem(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 4) responseToSend = SubArray(responseToSend, 0, 4);
            return responseToSend;
        }
        private byte msgAddFudanReadMem(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_ReadMem tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(ByteConv.EncodeU16(tagop.StartAddress));
            msg.AddRange(ByteConv.EncodeU16(tagop.length));
            return (byte)(msg.Count - tmp);
        }

        //Fudan Write Mem
        private byte[] Cmd_init_FudanWriteMem(UInt16 timeout, Gen2.Fudan.Fudan_WriteMem tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanWriteMem(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanWriteMem(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_WriteMem tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(ByteConv.EncodeU16(tagop.StartAddress));
            msg.Add((byte)tagop.length);
            msg.AddRange(tagop.data);
            return (byte)(msg.Count - tmp);
        }

        //Fudan State check
        private byte[] Cmd_init_FudanStateCheck(UInt16 timeout, Gen2.Fudan.Fudan_StateCheck tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanStateCheck(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanStateCheck(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_StateCheck tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(tagop.CmdConfig);
            return (byte)(msg.Count - tmp);
        }

        //Fudan Auth
        private byte[] Cmd_init_FudanAuth(UInt16 timeout, Gen2.Fudan.Fudan_Auth tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanAuth(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanAuth(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_Auth tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    msg.AddRange(ByteConv.EncodeU32(tagop.AccessPassword));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(tagop.CmdConfig);
            msg.AddRange(ByteConv.EncodeU32(tagop.AuthPassword));
            return (byte)(msg.Count - tmp);
        }

        //Fudan Start Stop Log
        private byte[] Cmd_init_FudanStartStopLog(UInt16 timeout, Gen2.Fudan.Fudan_StartStopLog tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanStartStopLog(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanStartStopLog(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_StartStopLog tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(tagop.CmdConfig);
            msg.AddRange(ByteConv.EncodeU32(tagop.AuthPassword));
            return (byte)(msg.Count - tmp);
        }

        //Fudan Read Measure
        private byte[] Cmd_init_FudanMeasure(UInt16 timeout, Gen2.Fudan.Fudan_Measure tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddFudanMeasure1(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            if (responseToSend.Length > 2) responseToSend = SubArray(responseToSend, 0, 2);
            return responseToSend;
        }
        private byte msgAddFudanMeasure1(ref List<byte> msg, UInt16 timeout, Gen2.Fudan.Fudan_Measure tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.AddRange(tagop.CmdConfig);
            msg.Add(tagop.StoreBlockAddress);//store block address of 1 byte
            return (byte)(msg.Count - tmp);
        }
        
        #endregion

        //Ilian Selection Tag
        private void Cmd_init_Ilian(UInt16 timeout, Gen2.Ilian.GEN2_ILN_TagSelectCommand tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddIlian(ref cmd, timeout, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            //int i = enableMultipleSelect ? 10 : 9;
            //byte[] responseToSend = SubArray(response, i, (response.Length - i));
            //if (responseToSend.Length < 2) responseToSend = SubArray(responseToSend, 0, 2);
            //return responseToSend;
        }
        private byte msgAddIlian(ref List<byte> msg, UInt16 timeout, Gen2.Ilian.GEN2_ILN_TagSelectCommand tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, tagop.AccessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.CommandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            return (byte)(msg.Count - tmp);
        }

        //EM4325GetSensorData
        private byte[] CmdEM4325GetSensorData(UInt16 timeout, UInt32 accessPassword, Gen2.EMMicro.EM4325.GetSensorData tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddEM4325GetSensorData(ref cmd, timeout, accessPassword, tagop, filter);
            byte[] response = SendTimeout(cmd, timeout);
            int i = enableMultipleSelect ? 10 : 9;
            byte[] responseToSend = SubArray(response, i, (response.Length - i));
            return responseToSend;
        }

        //msgAddEM4325GetSensorData
        private byte msgAddEM4325GetSensorData(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, Gen2.EMMicro.EM4325.GetSensorData tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.commandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.commandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.commandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.Add(tagop.bitsToSet);
            return (byte)(msg.Count - tmp);
        }

        //EM4325ResetAlarms
        private void CmdEM4325ResetAlarms(UInt16 timeout, UInt32 accessPassword, Gen2.EMMicro.EM4325.ResetAlarms tagop, TagFilter filter)
        {
            List<byte> cmd = new List<byte>();
            msgAddEM4325ResetAlarms(ref cmd, timeout, accessPassword, tagop, filter);
            SendTimeout(cmd, timeout);
        }

        //msgAddEM4325ResetAlarms
        private byte msgAddEM4325ResetAlarms(ref List<byte> msg, UInt16 timeout, UInt32 accessPassword, Gen2.EMMicro.EM4325.ResetAlarms tagop, TagFilter target)
        {
            SingulationBytes sb = MakeSingulationBytes(target, true, accessPassword);
            msg.Add(0x2D);
            int tmp = msg.Count;
            msg.AddRange(ByteConv.EncodeU16(timeout));
            msg.Add(tagop.ChipType);
            byte[] sbNewArray = new byte[sb.Mask.Length];
            Array.Copy(sb.Mask, 0, sbNewArray, 0, sb.Mask.Length);
            if (enableMultipleSelect && (!isEmbeddedTagOp))
            {
                msg.Add(SINGULATION_OPTION_MULTIPLE_SELECT);
            }
            if (!isMultiFilterEnabled)
            {
                msg.Add((byte)(0x40 | sb.Option));
                msg.AddRange(ByteConv.EncodeU16(tagop.commandCode));
                if (null != target)
                {
                    msg.AddRange(sb.Mask);
                }
            }
            else
            {
                if (isEmbeddedTagOp)
                {
                    msg.Add((byte)(0x40));
                    msg.AddRange(ByteConv.EncodeU16(tagop.commandCode));
                }
                else
                {
                    msg.Add((byte)(0x41));
                    msg.AddRange(ByteConv.EncodeU16(tagop.commandCode));
                    byte[] sbModified = new byte[sbNewArray.Length - 1];
                    Array.Copy(sbNewArray, 1, sbModified, 0, sbNewArray.Length - 1);
                    if (null != target)
                    {
                        msg.AddRange(sbModified);
                    }
                }
            }
            msg.Add(tagop.fillValue);
            return (byte)(msg.Count - tmp);
        }
        #endregion

        #endregion

        #region CRC Calculation Methods

        //*******************************************************
        //*              Calculates CRC                         *
        //*******************************************************

        #region CalcCRC

        /// <summary>
        /// Calculates CRC
        /// </summary>
        /// <param name="command">Byte Array that needs CRC calculation</param>
        /// <returns>CRC Byte Array</returns>
        private static byte[] CalcCRC(byte[] command)
        {
            UInt16 tempcalcCRC1 = CalcCRC8(65535, command[1]);
            tempcalcCRC1 = CalcCRC8(tempcalcCRC1, command[2]);
            byte[] CRC = new byte[2];

            if (command[1] != 0)
            {
                for (int i = 0; i < command[1]; i++)
                    tempcalcCRC1 = CalcCRC8(tempcalcCRC1, command[3 + i]);
            }

            CRC = BitConverter.GetBytes(tempcalcCRC1);

            Array.Reverse(CRC);

            return CRC;
        }

        #endregion

        #region CalcReturnCRC

        /// <summary>
        /// Calculates CRC of the data returned from the M5e,
        /// </summary>
        /// <param name="command">Byte Array that needs CRC calculation</param>
        /// <returns>CRC Byte Array</returns>
        private static byte[] CalcReturnCRC(byte[] command)
        {
            UInt16 tempcalcCRC1 = CalcCRC8(65535, command[1]);
            tempcalcCRC1 = CalcCRC8(tempcalcCRC1, command[2]);
            byte[] CRC = new byte[2];

            //if (command[1] != 0)
            {
                for (int i = 0; i < (command[1] + 2); i++)
                    tempcalcCRC1 = CalcCRC8(tempcalcCRC1, command[3 + i]);
            }

            CRC = BitConverter.GetBytes(tempcalcCRC1);

            Array.Reverse(CRC);

            return CRC;
        }

        #endregion

        #region CalcCRC8

        private static UInt16 CalcCRC8(UInt16 beginner, byte ch)
        {
            byte[] tempByteArray;
            byte xorFlag;
            byte element80 = new byte();
            element80 = 0x80;
            byte chAndelement80 = new byte();
            bool[] forxorFlag = new bool[16];

            for (int i = 0; i < 8; i++)
            {
                tempByteArray = BitConverter.GetBytes(beginner);
                Array.Reverse(tempByteArray);
                BitArray tempBitArray = new BitArray(tempByteArray);

                for (int j = 0; j < tempBitArray.Count; j++)
                    forxorFlag[j] = tempBitArray[j];

                Array.Reverse(forxorFlag, 0, 8);
                Array.Reverse(forxorFlag, 8, 8);

                for (int k = 0; k < tempBitArray.Count; k++)
                    tempBitArray[k] = forxorFlag[k];

                xorFlag = BitConverter.GetBytes(tempBitArray.Get(0))[0];
                beginner = (UInt16)(beginner << 1);
                chAndelement80 = (byte)(ch & element80);

                if (chAndelement80 != 0)
                    ++beginner;

                if (xorFlag != 0)
                    beginner = (UInt16)(beginner ^ 0x1021);

                element80 = (byte)(element80 >> 1);
            }

            return beginner;
        }

        #endregion

        #endregion

        #region Misc Utility Methods

        #region CheckRegion

        private void CheckRegion()
        {
            if ((Reader.Region)ParamGet("/reader/region/id") == Region.UNSPEC)
                throw new ReaderException("Region must be set before RF operation");
        }

        #endregion

        #region GetError

        private static void GetError(UInt16 error)
        {
            switch (error)
            {
                case 0:
                    break;

                case FAULT_MSG_WRONG_NUMBER_OF_DATA_Exception.StatusCode:
                    throw new FAULT_MSG_WRONG_NUMBER_OF_DATA_Exception();
                case FAULT_INVALID_OPCODE_Exception.StatusCode:
                    throw new FAULT_INVALID_OPCODE_Exception();
                case FAULT_UNIMPLEMENTED_OPCODE_Exception.StatusCode:
                    throw new FAULT_UNIMPLEMENTED_OPCODE_Exception();
                case FAULT_MSG_POWER_TOO_HIGH_Exception.StatusCode:
                    throw new FAULT_MSG_POWER_TOO_HIGH_Exception();
                case FAULT_MSG_INVALID_FREQ_RECEIVED_Exception.StatusCode:
                    throw new FAULT_MSG_INVALID_FREQ_RECEIVED_Exception();
                case FAULT_MSG_INVALID_PARAMETER_VALUE_Exception.StatusCode:
                    throw new FAULT_MSG_INVALID_PARAMETER_VALUE_Exception();

                case FAULT_MSG_POWER_TOO_LOW_Exception.StatusCode:
                    throw new FAULT_MSG_POWER_TOO_LOW_Exception();

                case FAULT_UNIMPLEMENTED_FEATURE_Exception.StatusCode:
                    throw new FAULT_UNIMPLEMENTED_FEATURE_Exception();
                case FAULT_INVALID_BAUD_RATE_Exception.StatusCode:
                    throw new FAULT_INVALID_BAUD_RATE_Exception();

                case FAULT_INVALID_REGION_Exception.StatusCode:
                    throw new FAULT_INVALID_REGION_Exception();

                case FAULT_INVALID_LICENSE_KEY_Exception.StatusCode:
                    throw new FAULT_INVALID_LICENSE_KEY_Exception();

                case FAULT_BL_INVALID_IMAGE_CRC_Exception.StatusCode:
                    throw new FAULT_BL_INVALID_IMAGE_CRC_Exception();

                case FAULT_NO_TAGS_FOUND_Exception.StatusCode:
                    throw new FAULT_NO_TAGS_FOUND_Exception();
                case FAULT_NO_PROTOCOL_DEFINED_Exception.StatusCode:
                    throw new FAULT_NO_PROTOCOL_DEFINED_Exception();
                case FAULT_INVALID_PROTOCOL_SPECIFIED_Exception.StatusCode:
                    throw new FAULT_INVALID_PROTOCOL_SPECIFIED_Exception();
                case FAULT_WRITE_PASSED_LOCK_FAILED_Exception.StatusCode:
                    throw new FAULT_WRITE_PASSED_LOCK_FAILED_Exception();
                case FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_NO_DATA_READ_Exception();
                case FAULT_AFE_NOT_ON_Exception.StatusCode:
                    throw new FAULT_AFE_NOT_ON_Exception();
                case FAULT_PROTOCOL_WRITE_FAILED_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_WRITE_FAILED_Exception();
                case FAULT_NOT_IMPLEMENTED_FOR_THIS_PROTOCOL_Exception.StatusCode:
                    throw new FAULT_NOT_IMPLEMENTED_FOR_THIS_PROTOCOL_Exception();
                case FAULT_PROTOCOL_INVALID_WRITE_DATA_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_INVALID_WRITE_DATA_Exception();
                case FAULT_PROTOCOL_INVALID_ADDRESS_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_INVALID_ADDRESS_Exception();
                case FAULT_GENERAL_TAG_ERROR_Exception.StatusCode:
                    throw new FAULT_GENERAL_TAG_ERROR_Exception();
                case FAULT_DATA_TOO_LARGE_Exception.StatusCode:
                    throw new FAULT_DATA_TOO_LARGE_Exception();
                case FAULT_PROTOCOL_INVALID_KILL_PASSWORD_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_INVALID_KILL_PASSWORD_Exception();
                case FAULT_PROTOCOL_KILL_FAILED_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_KILL_FAILED_Exception();

                case FAULT_AHAL_ANTENNA_NOT_CONNECTED_Exception.StatusCode:
                    throw new FAULT_AHAL_ANTENNA_NOT_CONNECTED_Exception();
                case FAULT_AHAL_TEMPERATURE_EXCEED_LIMITS_Exception.StatusCode:
                    throw new FAULT_AHAL_TEMPERATURE_EXCEED_LIMITS_Exception();
                case FAULT_AHAL_HIGH_RETURN_LOSS_Exception.StatusCode:
                    throw new FAULT_AHAL_HIGH_RETURN_LOSS_Exception();

                case FAULT_TAG_ID_BUFFER_NOT_ENOUGH_TAGS_AVAILABLE_Exception.StatusCode:
                    throw new FAULT_TAG_ID_BUFFER_NOT_ENOUGH_TAGS_AVAILABLE_Exception();
                case FAULT_TAG_ID_BUFFER_FULL_Exception.StatusCode:
                    throw new FAULT_TAG_ID_BUFFER_FULL_Exception();
                case FAULT_TAG_ID_BUFFER_REPEATED_TAG_ID_Exception.StatusCode:
                    throw new FAULT_TAG_ID_BUFFER_REPEATED_TAG_ID_Exception();
                case FAULT_TAG_ID_BUFFER_NUM_TAG_TOO_LARGE_Exception.StatusCode:
                    throw new FAULT_TAG_ID_BUFFER_NUM_TAG_TOO_LARGE_Exception();
                case FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception.StatusCode:
                    throw new FAULT_TAG_ID_BUFFER_AUTH_REQUEST_Exception();

                case FAULT_SYSTEM_UNKNOWN_ERROR_Exception.StatusCode:
                    throw new FAULT_SYSTEM_UNKNOWN_ERROR_Exception();
                case FAULT_FLASH_BAD_ERASE_PASSWORD_Exception.StatusCode:
                    throw new FAULT_FLASH_BAD_ERASE_PASSWORD_Exception();
                case FAULT_FLASH_BAD_WRITE_PASSWORD_Exception.StatusCode:
                    throw new FAULT_FLASH_BAD_WRITE_PASSWORD_Exception();
                case FAULT_FLASH_UNDEFINED_ERROR_Exception.StatusCode:
                    throw new FAULT_FLASH_UNDEFINED_ERROR_Exception();
                case FAULT_FLASH_ILLEGAL_SECTOR_Exception.StatusCode:
                    throw new FAULT_FLASH_ILLEGAL_SECTOR_Exception();
                case FAULT_FLASH_WRITE_TO_NON_ERASED_AREA_Exception.StatusCode:
                    throw new FAULT_FLASH_WRITE_TO_NON_ERASED_AREA_Exception();
                case FAULT_FLASH_WRITE_TO_ILLEGAL_SECTOR_Exception.StatusCode:
                    throw new FAULT_FLASH_WRITE_TO_ILLEGAL_SECTOR_Exception();
                case FAULT_FLASH_VERIFY_FAILED_Exception.StatusCode:
                    throw new FAULT_FLASH_VERIFY_FAILED_Exception();
                case FAULT_GEN2_PROTOCOL_OTHER_ERROR_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_OTHER_ERROR_Exception();
                case FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception();
                case FAULT_GEN2_PROTOCOL_MEMORY_LOCKED_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_MEMORY_LOCKED_Exception();
                case FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception();
                case FAULT_GEN2_PROTOCOL_NON_SPECIFIC_ERROR_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_NON_SPECIFIC_ERROR_Exception();
                case FAULT_GEN2_PROTOCOL_UNKNOWN_ERROR_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_UNKNOWN_ERROR_Exception();
                case FAULT_AHAL_INVALID_FREQ_Exception.StatusCode:
                    throw new FAULT_AHAL_INVALID_FREQ_Exception();
                case FAULT_AHAL_CHANNEL_OCCUPIED_Exception.StatusCode:
                    throw new FAULT_AHAL_CHANNEL_OCCUPIED_Exception();
                case FAULT_AHAL_TRANSMITTER_ON_Exception.StatusCode:
                    throw new FAULT_AHAL_TRANSMITTER_ON_Exception();
                case FAULT_TM_ASSERT_FAILED_Exception.StatusCode:
                    throw new FAULT_TM_ASSERT_FAILED_Exception("");
                case FAULT_PROTOCOL_BIT_DECODING_FAILED_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_BIT_DECODING_FAILED_Exception();
                case FAULT_PROTOCOL_INVALID_NUM_DATA_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_INVALID_NUM_DATA_Exception();
                case FAULT_PROTOCOL_INVALID_EPC_Exception.StatusCode:
                    throw new FAULT_PROTOCOL_INVALID_EPC_Exception();
                case FAULT_GEN2_PROTOCOL_V2_AUTHEN_FAILED_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_V2_AUTHEN_FAILED_Exception();
                case FAULT_GEN2_PROTOCOL_V2_UNTRACE_FAILED_Exception.StatusCode:
                    throw new FAULT_GEN2_PROTOCOL_V2_UNTRACE_FAILED_Exception();
                default:
                    throw new M5eStatusException(error);
            }
        }

        #endregion

        #region IntArraysEqual

        private static bool IntArraysEqual(int[] a, int[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        #endregion

        #region ArrayEquals

        /// <summary>
        /// C#'s Array.Equals only does instance equality,
        /// not content equality
        /// </summary>
        /// <typeparam name="T">Type of array elements</typeparam>
        /// <param name="a">Array to compare</param>
        /// <param name="b">Array to compare</param>
        /// <returns>True if array contents are equal, false otherwise.</returns>
        private static bool ArrayEquals<T>(T[] a, T[] b)
        {
            if (a == b)
                return true;

            if ((null == a) || (null == b))
                return false;

            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }

            return true;
        }

        #endregion

        #region BoolToByte

        private static byte BoolToByte(bool value)
        {
            return (byte)((true == value) ? 0x01 : 0x00);
        }

        /// <summary>
        /// Shorthand convenience method -- implicitly casts Object before passing to proper conversion method.
        /// </summary>
        /// <param name="value">Object that can be cast to bool</param>
        /// <returns>Byte representing value</returns>
        private static byte BoolToByte(Object value)
        {
            return BoolToByte((bool)value);
        }

        #endregion

        #region ByteToBool

        private static bool ByteToBool(byte value)
        {
            return (0x00 == value) ? false : true;
        }

        #endregion

        #region ByteToBool

        /// <summary>
        /// Shorthand convenience method -- implicitly casts Object before passing to proper conversion method.
        /// </summary>
        /// <param name="value">Object that can be cast to byte</param>
        /// <returns>bool representing value</returns>
        private static bool ByteToBool(Object value)
        {
            return ByteToBool((byte)value);
        }

        #endregion

        #region ByteArrayToIntArray

        private static int[] ByteArrayToIntArray(byte[] ina)
        {
            int[] outa = new int[ina.Length];
            for (int i = 0; i < ina.Length; i++) { outa[i] = ByteToInt(ina[i]); }
            return outa;
        }

        #endregion

        #region IntArrayToByteArray

        private static byte[] IntArrayToByteArray(int[] ina)
        {
            byte[] outa = new byte[ina.Length];

            for (int i = 0; i < ina.Length; i++)
                outa[i] = IntToByte(ina[i]);

            return outa;
        }

        #endregion

        #region ByteToInt

        /// <summary>
        /// Convert byte to int.
        /// </summary>
        /// <param name="value">Value to convert.  Contents must fit within a byte (0-255).</param>
        /// <returns>Byte-typed value</returns>
        private static int ByteToInt(byte value)
        {
            return (int)value;
        }

        #endregion

        #region IntToByte

        /// <summary>
        /// Convert int to byte.
        /// </summary>
        /// <param name="value">Value to convert.  Contents must fit within a byte (0-255).</param>
        /// <returns>Byte-typed value</returns>
        private static byte IntToByte(int value)
        {
            if ((value < 0) || (255 < value))
                throw new ArgumentException(String.Format("Value ({0:D}) too big to convert to byte (0-255)", value));

            return (byte)value;
        }

        #endregion

        #region UriPathToCom

        /// <summary>
        /// Convert URI-style path (/COM123) to Windows format (COM123)
        /// </summary>
        /// <param name="uriPath">URI-style path; e.g., "/COM123"</param>
        /// <returns>Windows-style COM port name; e.g., "COM123"
        /// Note: .NET does not require special prefixes to handle ports above COM9.
        /// Only Win32 requires the \\.\ prefix.</returns>
        private static string UriPathToCom(string uriPath)
        {
            if (uriPath.ToUpper().StartsWith("/COM"))
                return uriPath.Substring(1);
            return uriPath;
        }

        #endregion

        #region CropBuf

        private static byte[] CropBuf(byte[] fullbuf, int length)
        {
            byte[] cropped = new byte[length];
            Array.Copy(fullbuf, cropped, length);
            return cropped;
        }

        #endregion

        #region FourByteDateToString

        private static string FourByteDateToString(byte[] data, int offset)
        {
            string[] byteStrs = FourByteVersionToFourStrings(data, offset);
            return String.Join("", byteStrs);
        }

        #endregion

        #region FourByteVersionToFourStrings

        private static string[] FourByteVersionToFourStrings(byte[] data, int offset)
        {
            string[] strs = new string[4];

            for (int i = 0; i < 4; i++)
                strs[i] = data[offset + i].ToString("X2");

            return strs;
        }

        #endregion

        #region FourByteVersionToString

        private static string FourByteVersionToString(byte[] data, int offset)
        {
            return String.Join(".", FourByteVersionToFourStrings(data, offset));
        }

        #endregion

        #region MakeEmptySingulationBytes
        /// <summary>
        /// "No singulation desired".  Cannot be used with commands that use Access Password.
        /// For some reason, turning off singulation (option 0x00) also turns off the access password field.
        /// Only commands that do not include an access password may use this form  of SingulationBytes.
        /// </summary>
        /// <returns>Singulation bytes for option 0x00</returns>
        private static SingulationBytes MakeEmptySingulationBytes()
        {
            SingulationBytes sb = new SingulationBytes();
            sb.Option = 0x00;
            sb.Mask = new byte[0];
            return sb;
        }

        #endregion

        #region MakeSingulationBytes

        /// <summary>
        /// Even if no singulation is desired, you still have to provide
        /// the "singulation option" byte that says, "No singulation, please."
        /// </summary>
        /// <returns></returns>
        private SingulationBytes MakePasswordSingulationBytes(UInt32 accessPassword)
        {
            SingulationBytes sb = new SingulationBytes();
            sb.Option = 0x05;  // Password only
            List<byte> tempList = new List<byte>();
            tempList.AddRange(ByteConv.EncodeU32(accessPassword));
            if (enableMultipleSelect)
            {
                tempList.Add((byte)Gen2.Select.Target.Select);
                tempList.Add((byte)Gen2.Select.Action.ON_N_OFF);
                tempList.Add(0x00);
            }
            sb.Mask = tempList.ToArray();

            return sb;
        }

        /// <summary>
        /// Singulate on entire EPC.  Omit address field.
        /// </summary>
        /// <param name="list">The list sturcture to hold the temporary singulation bytes</param>
        /// <param name="epc">EPC to singulate.</param>
        /// <returns>Singulation specification bytes for M5e protocol.</returns>
        private static SingulationBytes MakeSingulationBytes(List<byte> list, byte[] epc)
        {
            SingulationBytes sb = new SingulationBytes();
            sb.Option = 0x01;

            uint bitCount = (uint)(epc.Length * 8);

            //if epc is extended epc
            if (bitCount > 255)
            {
                sb.Option |= 0x20; //OPTION for extended length  is 0x20
                list.Add(Convert.ToByte((bitCount >> 8) & 0xff));
            }
            list.Add(Convert.ToByte(bitCount & 0xff));
            list.AddRange(epc);

            sb.Mask = list.ToArray();
            return sb;
        }

        /// <summary>
        /// Singulate on entire EPC.  Omit address field.
        /// </summary>
        /// <param name="tf">TagFilter with singulation parameters.  May be null.</param>
        /// <param name="needAccessPassword">Do you need the form that supports an access password?
        /// Pre-Sontag firmware has a protocol design flaw -- to specify an access password, you also have to provide singulation bytes.
        /// The workaround is to provide a select with empty mask.</param>
        /// <param name="accessPassword">The tag access password</param>
        /// <returns>Singulation specification bytes for M5e protocol.  If tf is null, returns default singulation.</returns>
        private SingulationBytes MakeSingulationBytes(TagFilter tf, bool needAccessPassword,
                                                             UInt32 accessPassword)
        {
            if (null == tf)
            {
                if (needAccessPassword == false || accessPassword == 0)
                {
                    return MakeEmptySingulationBytes();
                }
                else
                {
                    return MakePasswordSingulationBytes(accessPassword);
                }
            }
            List<byte> list = new List<byte>();
            if (needAccessPassword)
            {
                list.AddRange(ByteConv.EncodeU32(accessPassword));
            }
            if ((tf is Gen2.Select) && (!isModelM3e)) // For M3e, Gen2.Select filter is not supported
            {
                Gen2.Select sel = (Gen2.Select)tf;
                return MakeSingulationBytes(list, sel.Bank, sel.BitPointer, sel.BitLength, sel.Mask, sel.Invert, (byte)sel.target, (byte)sel.action);
            }
            else if ((tf is Iso180006b.TagData) && (!isModelM3e)) // For M3e, Iso180006b.TagData filter is not supported
            {
                return MakeIso180006bSingulationBytes(tf);
            }
            else if ((tf is TagData) && (!isModelM3e)) // For M3e, TagData filter is not supported
            {
                TagData filterEPCSingulation = (TagData)tf;
                return MakeSingulationBytes(list, filterEPCSingulation.EpcBytes);
            }
            else if ((tf is Iso180006b.Select) && (!isModelM3e)) // For M3e, Iso180006b.Select filter is not supported
            {
                Iso180006b.Select sel = (Iso180006b.Select)tf;
                return MakeIso180006bSingulationBytes(sel);
            }
            else if ((tf is Select_TagType) && (isModelM3e)) // Select_TagType filter is supported for M3e only.
            {
                Select_TagType tagtypeFilter = (Select_TagType)tf;
                return MakeSelectSingulationBytes(list, tagtypeFilter);
            }
            else if ((tf is Select_UID) && (isModelM3e)) //Select_UID filter is supported for M3e only.
            {
                Select_UID tagtypeFilter = (Select_UID)tf;
                return MakeSelectSingulationBytes(list, tagtypeFilter);
            }
            else if (tf is MultiFilter)
            {
                MultiFilter multiFilter = (MultiFilter)tf;
                TagFilter[] filters = multiFilter.filters;
                if (isModelM3e && (filters.Length > 2))
                {
                    throw new ArgumentException("Unsupported Operation. Only 2 filters are supported.");
                }
                SingulationBytes sb = new SingulationBytes();
                SingulationBytes sb1 = new SingulationBytes();
                SingulationBytes sb2 = new SingulationBytes();
                SingulationBytes sb3 = new SingulationBytes();
                int length;
                byte[] combined;
                // throws exception if multi select is not supported on the current firmware version. 
                // But for M3e, we don't validate using firmware version as it is a new product.
                if ((!enableMultipleSelect) && (!isModelM3e))
                {
                    throw new ArgumentException("Unsupported Operation.");
                }
                // throws an exception when multi-select contains filter type as TagData as it supports only Gen2.Select.
                foreach (TagFilter filter in filters)
                {
                    if (filter is TagData)
                    {
                        throw new ArgumentException("Unsupported Operation.");
                    }
                    // validate each filter passed in multi filter is supported or not.
                    if (isModelM3e && (!(filter is Select_TagType) && !(filter is Select_UID)))
                    {
                        throw new ArgumentException("Unsupported Operation. Supports only Select_TagType or Select_UID");
                    }
                }
                switch (filters.Length)
                {
                    case 1:
                        return MakeSingulationBytes(filters[0], needAccessPassword, accessPassword);
                    case 2:
                        isMultiFilterEnabled = true;
                        sb1 = MakeSingulationBytes(filters[0], needAccessPassword, accessPassword);
                        sb2 = MakeSingulationBytes(filters[1], false, accessPassword);
                        length = sb1.Mask.Length + sb2.Mask.Length;
                        combined = new byte[length + 1];
                        combined[0] = sb1.Option;
                        System.Buffer.BlockCopy(sb1.Mask, 0, combined, 1, sb1.Mask.Length - 1);
                        combined[sb1.Mask.Length] = sb2.Option;
                        System.Buffer.BlockCopy(sb2.Mask, 0, combined, sb1.Mask.Length + 1, sb2.Mask.Length);
                        sb.Mask = combined;
                        return sb;
                    case 3:
                        if (isModelM3e)
                        {
                            throw new ArgumentException("Filters cannot be more than 2");
                        }
                        else
                        {
                            isMultiFilterEnabled = true;
                            sb1 = MakeSingulationBytes(filters[0], needAccessPassword, accessPassword);
                            sb2 = MakeSingulationBytes(filters[1], false, accessPassword);
                            sb3 = MakeSingulationBytes(filters[2], false, accessPassword);
                            length = sb1.Mask.Length + sb2.Mask.Length + sb3.Mask.Length;
                            combined = new byte[length + 1];
                            combined[0] = sb1.Option;
                            System.Buffer.BlockCopy(sb1.Mask, 0, combined, 1, sb1.Mask.Length - 1);
                            combined[sb1.Mask.Length] = sb2.Option;
                            System.Buffer.BlockCopy(sb2.Mask, 0, combined, sb1.Mask.Length + 1, sb2.Mask.Length);
                            combined[sb2.Mask.Length + sb1.Mask.Length] = sb3.Option;
                            System.Buffer.BlockCopy(sb3.Mask, 0, combined, sb1.Mask.Length + sb2.Mask.Length + 1, sb3.Mask.Length);
                            sb.Mask = combined;
                        }
                        return sb;
                    default:
                        throw new ArgumentException("Filters cannot be more than 2");
                }
            }
            else
                throw new ArgumentException("Unknown select type " + tf.GetType().ToString());
        }


        /// <summary>
        /// Specify all singulation fields.
        /// </summary>
        /// <param name="list">The list sturcture to hold the temporary singulation bytes</param>
        /// <param name="bank">Tag memory bank to singulate against</param>
        /// <param name="address">Bit address at which to start comparison</param>
        /// <param name="bitCount">Number of bits to compare</param>
        /// <param name="data">Bits to match</param>
        /// <param name="invert">Invert selection?
        /// <param name="target">target</param>
        /// <param name="action">action</param>
        /// If true, tags NOT matching the mask will be selected.
        /// If false, tags matching mask are selected (normal behavior)</param>
        /// <returns>Singulation specification bytes for M5e protocol.</returns>
        private SingulationBytes MakeSingulationBytes(List<Byte> list, Gen2.Bank bank, UInt32 address, UInt16 bitCount, ICollection<byte> data, bool invert, byte target, byte action)
        {
            SingulationBytes sb = new SingulationBytes();
            byte option = BankToSelectOption(bank);

            if (bank != Gen2.Bank.GEN2EPCLENGTHFILTER)
            {
                if (invert)
                    option |= 0x08;

                if (bitCount > 255)
                    option |= 0x20;

                //If gen2 secure access is enabled make option as 0x44 else 0x04
                if (isSecurePasswordLookupEnabled)
                {
                    option |= 0x40;
                }
                sb.Option = option;
                list.AddRange(ByteConv.EncodeU32(address));

                if (bitCount > 255)
                    list.AddRange(ByteConv.EncodeU16(bitCount));
                else
                    list.Add((byte)bitCount);

                if (0 < bitCount)
                    list.AddRange(Truncate(data, ByteConv.BytesPerBits(bitCount)));

                sb.Mask = list.ToArray();
            }
            else
            {
                list.Clear();
                list.AddRange(ByteConv.EncodeU16(bitCount));
                sb.Option = option;
                sb.Mask = list.ToArray();
            }
            if (enableMultipleSelect)
            {
                list.Add(target);
                list.Add(action);
                list.Add(0x00);
                sb.Mask = list.ToArray();
            }
            return sb;
        }

        #endregion

        #region MakeIso180006bSingulationBytes

        /// <summary>
        /// Create data for ISO180006B select operations based on a tag filter
        /// </summary>
        /// <param name="tf">TagFilter with singulation parameters.  May be null.</param>
        /// <returns>Singulation specification bytes for M5e protocol.  If tf is null, returns match-anything singulation.</returns>
        private static SingulationBytes MakeIso180006bSingulationBytes(TagFilter tf)
        {
            List<Byte> list = new List<Byte>();
            SingulationBytes sb = new SingulationBytes();

            if (null == tf)
            {
                // Set up a match-anything filter, since ISO180006B commands
                // generally don't support not having a filter at all
                list.Add(Iso180006bSelectOpToOption(Iso180006b.SelectOp.EQUALS));
                list.Add(0); // Address
                list.Add(0); // Mask - don't compare any bytes
                for (int i = 0; i < 8; i++)
                {
                    list.Add(0); // Match byte
                }
            }
            else if (tf is Iso180006b.Select)
            {
                sb.Option = 1;
                Iso180006b.Select sel = (Iso180006b.Select)tf;

                byte opByte = Iso180006bSelectOpToOption(sel.Op);
                if (sel.Invert)
                    opByte |= 4;
                list.Add(opByte);
                list.Add(sel.Address);
                list.Add(sel.Mask);
                list.AddRange(sel.Data);
            }
            else if (tf is TagData)
            {
                TagData td = (TagData)tf;
                int size = td.EpcBytes.Length;
                sb.Option = 1;
                if (size > 8)
                {
                    throw new ArgumentException("Tag data too long for ISO180006B filter: " + size + " bytes");
                }
                list.Add(Iso180006bSelectOpToOption(Iso180006b.SelectOp.EQUALS));
                list.Add(0); // Address - EPC is at the start of memory
                list.Add((byte)((0xff00 >> size) & 0xff)); // Convert the byte count to a MSB-based bit mask
                list.AddRange(td.EpcBytes);
                for (; size < 8; size++)
                {
                    list.Add(0); // Pad out to 8 mask bytes
                }
            }

            sb.Mask = list.ToArray();
            return sb;
        }

        private static byte Iso180006bSelectOpToOption(Iso180006b.SelectOp op)
        {
            switch (op)
            {
                case Iso180006b.SelectOp.EQUALS:
                    return 0;
                case Iso180006b.SelectOp.NOTEQUALS:
                    return 1;
                case Iso180006b.SelectOp.LESSTHAN:
                    return 2;
                case Iso180006b.SelectOp.GREATERTHAN:
                    return 3;
                default:
                    throw new ArgumentException("Unrecognized SelectOp value: " + op.ToString());
            }
        }

        #endregion

        #region BankToSelectOption

        /// <summary>
        /// Translate target memory bank to singulation option.  Does not include options
        /// 0x00 (no singulation) and 0x01 (singulate on entire EPC).
        /// </summary>
        /// <param name="bank">Tag memory bank to singulate against</param>
        /// <returns>Tag Singulation option code</returns>
        private static byte BankToSelectOption(Gen2.Bank bank)
        {
            switch (bank)
            {
                case Gen2.Bank.TID:
                    return 0x02;
                case Gen2.Bank.USER:
                    return 0x03;
                case Gen2.Bank.EPC:
                    return 0x04;
                case Gen2.Bank.GEN2EPCLENGTHFILTER:
                    return 0x06;
                case Gen2.Bank.GEN2EPCTRUNCATE:
                    return 0x07;
                default:
                    throw new ArgumentException("Unrecognized MemBank value: " + bank.ToString());
            }
        }

        #endregion

        #region MakeSelectSingulationBytes

        /// <summary>
        /// Create data for select operations based on a tag filter that is Tagtype based or UID based or both
        /// </summary>
        /// <param name="list">List containing singulation bytes for filter</param>
        /// <param name="tf">TagFilter with singulation parameters</param>
        /// <returns>Singulation specification bytes for ISO15693 protocol.</returns>
        private static SingulationBytes MakeSelectSingulationBytes(List<Byte> list, TagFilter tf)
        {
            SingulationBytes sb = new SingulationBytes();
            if (tf is Select_TagType)
            {
                sb.Option = (byte)SingulationOptions.SELECT_ON_TAGTYPE; // Option for Tagtype based filter
                Select_TagType tagTypeSel = (Select_TagType)tf;
                list.AddRange(ConvertToEBV(tagTypeSel.tagType));
                list.Add(0x00); // End of select filter indicator
            }
            else if (tf is Select_UID)
            {
                sb.Option = (byte)SingulationOptions.SELECT_ON_UID; // Option for UID based filter
                Select_UID uidSel = (Select_UID)tf;
                list.Add((byte)uidSel.bitLength);
                list.AddRange(Truncate(uidSel.uidMask, ByteConv.BytesPerBits(uidSel.bitLength)));
                list.Add(0x00); // End of select filter indicator
            }
            sb.Mask = list.ToArray();
            return sb;
        }
        #endregion //MakeSelectTagTypeSingulationBytes

        #region Truncate

        /// <summary>
        /// Trim byte array to specified size.
        /// </summary>
        /// <param name="inBytes">Input byte array</param>
        /// <param name="byteCount">Desired size of output byte array</param>
        /// <returns>First byteCount bytes of input array.  If byteCount is same as inBytes.Length, a direct reference is returned.</returns>
        private static byte[] Truncate(ICollection<byte> inBytes, int byteCount)
        {
            if (byteCount > inBytes.Count)
                throw new ArgumentOutOfRangeException("byteCount can't be greater than inBytes.Length");

            byte[] inBytesArray = CollUtil.ToArray(inBytes);

            if (byteCount == inBytesArray.Length)
                return inBytesArray;

            byte[] outBytes = new byte[byteCount];
            Array.Copy(inBytesArray, outBytes, byteCount);
            return outBytes;
        }
        #endregion

        #region IsOdd

        private static bool IsOdd(int val)
        {
            return 1 == (val & 1);
        }

        #endregion

        #region SubArray
        /// <summary>
        /// Extract subarray
        /// </summary>
        /// <param name="src">Source array</param>
        /// <param name="offset">Start index in source array</param>
        /// <param name="length">Number of source elements to extract</param>
        /// <returns>New array containing specified slice of source array</returns>
        private static byte[] SubArray(byte[] src, int offset, int length)
        {
            return SubArray(src, ref offset, length);
        }

        /// <summary>
        /// Extract subarray, automatically incrementing source offset
        /// </summary>
        /// <param name="src">Source array</param>
        /// <param name="offset">Start index in source array.  Automatically increments value by copied length.</param>
        /// <param name="length">Number of source elements to extract</param>
        /// <returns>New array containing specified slice of source array</returns>
        private static byte[] SubArray(byte[] src, ref int offset, int length)
        {
            byte[] dst = new byte[length];
            Array.Copy(src, offset, dst, 0, length);
            offset += length;
            return dst;
        }

        #endregion

        #region gen2BLFintToObject

        private static Object gen2BLFintToObject(int val)
        {
            switch (val)
            {
                case 250:
                    {
                        return serialGen2LinkFrequency.LINK250KHZ;
                    }
                case 320:
                    {
                        return serialGen2LinkFrequency.LINK320KHZ;
                    }
                case 640:
                    {
                        return serialGen2LinkFrequency.LINK640KHZ;
                    }
                default:
                    {
                        throw new ArgumentException("Unsupported tag BLF:" + val.ToString() + "kHz");
                    }
            }
        }

        #endregion

        #region gen2BLFObjectToInt

        private static int gen2BLFObjectToInt(Object val)
        {
            switch ((serialGen2LinkFrequency)val)
            {
                case serialGen2LinkFrequency.LINK250KHZ:
                    {
                        return 250;
                    }
                case serialGen2LinkFrequency.LINK320KHZ:
                    {
                        return 320;
                    }
                case serialGen2LinkFrequency.LINK640KHZ:
                    {
                        return 640;
                    }
                default:
                    {
                        throw new ArgumentException("Unsupported tag BLF.");
                    }
            }
        }
        #endregion

        #region iso180006BBLFintToObject

        private static Object iso18000BBLFintToObject(int val)
        {
            switch (val)
            {
                case 40:
                    {
                        return serialIso180006bLinkFrequency.LINK40KHZ;
                    }
                case 160:
                    {
                        return serialIso180006bLinkFrequency.LINK160KHZ;
                    }
                default:
                    {
                        throw new ArgumentException("Unsupported tag BLF:" + val.ToString() + "kHz");
                    }
            }

        }

        #endregion

        #region iso18000BBLFObjectToInt

        private static int iso18000BBLFObjectToInt(Object val)
        {
            switch ((serialIso180006bLinkFrequency)val)
            {
                case serialIso180006bLinkFrequency.LINK40KHZ:
                    {
                        return 40;
                    }
                case serialIso180006bLinkFrequency.LINK160KHZ:
                    {
                        return 160;
                    }
                default:
                    {
                        throw new ArgumentException("Unsupported tag BLF.");
                    }
            }

        }

        #endregion

        #endregion

        /// <summary>
        /// Name of serial port (for informational purposes only --
        /// Do not expose to user unless absolutely necessary)
        /// </summary>
        public string PortName { get { return _serialPort.PortName; } }

        /// <summary>
        /// parseEBVData function parses the response available in EBV format from the specified readoffset 
        /// and returns the data in byte array. readoffset variable is updated according the flags retrieved.
        /// </summary>
        public static byte[] parseEBVData(byte[] response, ref int readOffset)
        {
           /*  The response is in EBV format. Hence the number of bytes is not fixed. 
            *  Number of bytes to extract depends on the MSB of the retrieved byte.
            */
            
            List<byte> ebvData = new List<byte>();

            // copy the extracted first byte to ebvData.
            ebvData.Add(response[readOffset]);

            /* If MSB of retrieved byte is set, then extract next byte and add it to ebvData. 
             * Repeat this process until 5 bytes of EBV data received since maximum size is 5 bytes.
             */
            if (((byte)(0x80) & ByteConv.GetU8(response, readOffset++)) == (byte)0x80)
            {
                ebvData.Add(response[readOffset]);
                if (((byte)(0x80) & ByteConv.GetU8(response, readOffset++)) == (byte)0x80)
                {
                    ebvData.Add(response[readOffset]);
                    if (((byte)(0x80) & ByteConv.GetU8(response, readOffset++)) == (byte)0x80)
                    {
                        ebvData.Add(response[readOffset]);
                        if (((byte)(0x80) & ByteConv.GetU8(response, readOffset++)) == (byte)0x80)
                        {
                            ebvData.Add(response[readOffset]);
                            readOffset++;
                        }
                    }
                }
            }
            return ebvData.ToArray();
        }
    }
}
