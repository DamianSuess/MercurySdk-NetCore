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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
#if !WindowsCE
using Org.LLRP.LTK.LLRPV1;
#endif
using System.IO;

namespace ThingMagic
{
    /// <summary>
    /// Abstract base class for ThingMagic RFID reader devices.
    /// </summary>
    public abstract class Reader : Disposable
    {
        /// <summary>
        /// To fetch read tags
        /// </summary>
        public bool fetchTagReads = false;
        /// <summary>
        /// To add Sleep
        /// </summary>
        public bool OffTimeAdded = false;
        /// <summary>
        /// to check mulitple readers connected
        /// </summary>
        public bool NoofMultiReaders = false;
        /// <summary>
        /// Variable to hold the status of reader exception if any occurred.
        /// </summary>
        public ReaderException lastReportedException = null;

        /// <summary>
        /// Debug logging interface for Reader and subclasses
        /// </summary>
        protected interface DebugLog
        {
            /// <summary>
            /// Log a message to disk
            /// </summary>
            /// <param name="message">Message to log</param>
            void Log(string message);
        }
        /// <summary>
        /// Send debug log to disk
        /// </summary>
        protected class DiskLog : DebugLog
        {
            StreamWriter log;
            /// <summary>
            /// Create a disk-based (persistent) debug log
            /// </summary>
            /// <param name="filename"></param>
            public DiskLog(string filename)
            {
                // Add unique string to filename to allow multiple processes that use the same classes
                // TODO: Come up with a more controlled way to disambiguate processes
                filename = Environment.TickCount.ToString() + "-" + filename;
                log = new StreamWriter(filename, true);
            }
            /// <summary>
            /// Log a message to disk
            /// </summary>
            /// <param name="message"></param>
            public void Log(string message)
            {
                string text = Environment.TickCount + " " + message + "\r\n";
                log.Write(text);
                log.Flush();
            }
        }
        /// <summary>
        /// Dummy DiskLog class for disabling logging without removing debug statements
        /// </summary>
        protected class DummyDebugLog : DebugLog
        {
            /// <summary>
            /// Create a logger that outputs to nowhere
            /// </summary>
            public DummyDebugLog()
            {
            }
            /// <summary>
            /// Log a message to nowhere
            /// </summary>
            /// <param name="message"></param>
            public void Log(string message)
            {
            }
        }
        // Declare log object statically.
        // If we try to create a new one per Reader, reader creations tend to fail
        // because the last instance is still holding a lock on the file.
        /// <summary>
        /// Debug log object for Reader and subclasses
        /// </summary>
//#if DEBUG
//        protected static DiskLog debug = new DiskLog("Reader.log");
//#else
        protected static DebugLog debug = new DummyDebugLog();
//#endif

        void debugln(string msg)
        {
            //notifyExceptionListeners(new ReaderException("DEBUG " + msg));
            debug.Log(msg);
        }

        #region Delegates

        /// <summary>
        /// Parameter setting filter.  Modifies parameter value on the way in or out of the Setting object.
        /// </summary>
        /// <param name="value">Input parameter:
        /// For get, this comes from Setting.Value.
        /// For set, this is the input argument to the Set method.</param>
        /// <returns>Filtered parameter:
        /// For get, this object will be presented to the user.
        /// For set, this object will be saved in Setting.Value.</returns>
        protected delegate Object SettingFilter(Object value);
        /// <summary>
        /// Wait till ThreadPoolCallBack method finishes its execution
        /// </summary>
        ManualResetEvent releaseCallBackMethod = new ManualResetEvent(false);

        #endregion

        #region Nested Enums
        /// <summary>
        /// RFID regulatory regions
        /// </summary>
        public enum Region
        {
            /// <summary>
            /// Region not set
            /// </summary>
            UNSPEC = 0,
            /// <summary>
            /// North America
            /// </summary>
            NA = 1,
            /// <summary>
            /// Europe, version 1 (LBT)
            /// </summary>
            EU = 2,
            /// <summary>
            /// Korea
            /// </summary>
            KR = 3,
            /// <summary>
            /// India
            /// </summary>
            IN = 4,
            /// <summary>
            /// Japan
            /// </summary>
            JP = 5,
            /// <summary>
            /// People's Republic of China (mainland)
            /// </summary>
            PRC = 6,
            /// <summary>
            /// Europe, version 2 (??)
            /// </summary>
            EU2 = 7,
            /// <summary>
            /// Europe, version 3 (no LBT)
            /// </summary>
            EU3 = 8,
            /// <summary>
            /// Korea (revised)
            /// </summary>
            KR2 = 9,
            /// <summary>
            /// PRC with 875KHZ
            /// </summary>
            PRC2 = 10,
            /// <summary>
            /// Australia
            /// </summary>
            AU = 11,
            /// <summary>
            /// New Zealand !!EXPERIMENTAL!!
            /// </summary>
            NZ = 12,
            /// <summary>
            /// Reduced FCC region
            /// </summary>
            NA2 = 13,
            /// <summary>
            /// NA3
            /// </summary>
            NA3 = 14,
            /// <summary>
            /// IS region applicable for Micro, M6e-JIC, M6ePlus
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
            UNIVERSAL=31,
            /// <summary>
            /// Israel2(IS2) region applicable for Micro and Nano 
            /// </summary>
            IS2 = 32,
            /// <summary>
            /// NA4 region applicable for Micro and M6e 
            /// </summary>
            NA4 = 33,
            /// <summary>
            ///OPEN region with extended frequency range 840-960MHz for M6ePlus module
            /// </summary>
            OPEN_EXTENDED = 0xFE,
            /// <summary>
            ///Unrestricted access to full hardware range  
            /// </summary>
            OPEN = 0xFF,  
        };

        /// <summary>
        /// Reader Status Flag enum
        /// </summary>
        public enum ReaderStatusFlag
        {
            /// <summary>
            /// Noise Floor
            /// </summary>
            NOISE_FLOOR = 0x0001,
            /// <summary>
            /// Frequency
            /// </summary>
            FREQUENCY = 0x00002,
            /// <summary>
            /// Temperature
            /// </summary>
            TEMPERATURE = 0x00004,
            /// <summary>
            /// Current Antenna Ports
            /// </summary>
            CURRENT_ANTENNAS = 0x0008,
            /// <summary>
            /// All
            /// </summary>
            ALL = 0x000F

        }

        #region PowerMode

        /// <summary>
        /// enum to define different power modes.
        /// </summary>
        public enum PowerMode
        {
            /// <summary>
            /// Invalid Power Mode
            /// </summary>
            INVALID = -1,
            /// <summary>
            /// Full Power Mode
            /// </summary>
            FULL = 0,
            /// <summary>
            /// Minimal Saving Mode
            /// </summary>
            MINSAVE = 1,
            /// <summary>
            /// Medium Saving Mode
            /// </summary>
            MEDSAVE = 2,
            /// <summary>
            /// Maximum Saving Mode
            /// </summary>
            MAXSAVE = 3,
            /// <summary>
            /// Maximum Saving Mode
            /// </summary>
            SLEEP = 4,
        }

        #endregion

        #region Regulatory Mode
        /// <summary>
        /// enum to define Regulatory Mode
        /// </summary>
        public enum RegulatoryMode
        {
            /// <summary>
            /// Continous Mode
            /// </summary>
            CONTINUOUS,
            /// <summary>
            /// Timed Mode
            /// </summary>
            TIMED,
        }
        #endregion

        #region Regulatory Modulation
        /// <summary>
        /// enum to define Regulatory Mode
        /// </summary>
        public enum RegulatoryModulation
        {
            /// <summary>
            /// Continous Wave (CW)
            /// </summary>
            CW = 1,
            /// <summary>
            /// PRBS
            /// </summary>
            PRBS = 2,            
        }
        #endregion

        #region FeaturesFlag
        /// <summary>
        /// enum to define Features.
        /// </summary>
        public enum ReaderFeaturesFlag
        {
            /// <summary>
            /// None
            /// </summary>
            READER_FEATURES_FLAG_NONE = 0,
            /// <summary>
            /// Duty Cycle
            /// </summary>
            READER_FEATURES_FLAG_DUTY_CYCLE = 1,
            /// <summary>
            /// Multi Select
            /// </summary>
            READER_FEATURES_FLAG_MULTI_SELECT = 2,
            /// <summary>
            /// Antenna read time
            /// </summary>
            READER_FEATURES_FLAG_ANTENNA_READ_TIME = 4,
            /// <summary>
            /// Logical antenna extention(to 64 from 32) feature support flag
            /// </summary>
            READER_FEATURES_FLAG_EXTENDED_LOGICAL_ANTENNA = 8,
            /// <summary>
            /// All Features
            /// </summary>
            READER_FEATURES_FLAG_ALL = 15,
            /// <summary>
            /// Custom region configuration
            /// </summary>
            READER_FEATURES_FLAG_CUSTOM_REGION = 32,
            /// <summary>
            /// Serial Reader (M3e)
            /// </summary>
            READER_FEATURES_FLAG_ADDR_BYTE_EXTENSION=64,

        }
        #endregion


        #endregion

        #region Nested Classes

        #region TagReadCallback

        /// <summary>
        /// ThreadPool-compatible wrapper for servicing asynchronous reads
        /// </summary>
        private sealed class TagReadCallback
        {
            #region Fields

            private Reader      _rdr;          
            private ICollection<TagReadData> _reads;

            #endregion

            #region Construction

            /// <summary>
            /// Create ThreadPool-compatible wrapper
            /// </summary>
            /// <param name="rdr">Reader object that will service TagReadData event</param>
            /// <param name="reads">TagReadData event to servic e</param>
         
            public TagReadCallback(Reader rdr, ICollection<TagReadData> reads)
            {
                _rdr       = rdr;
                _reads     = reads;
            }

            #endregion

            #region ThreadPoolCallBack

            /// <summary>
            /// ThreadPool-compatible callback to be passed to ThreadPool.QueueUserWorkItem
            /// </summary>
            /// <param name="threadContext">Identifier of thread that is servicing this callback</param>
            public void ThreadPoolCallBack(Object threadContext)
            {
                ManualResetEvent releaseCallBackMethod = (ManualResetEvent)threadContext;
                try
                {
                foreach (TagReadData read in _reads)
                {
                    read.Reader = _rdr;
                    _rdr.OnTagRead(read);
                }
                }
                catch (Exception ex)
                {
                    if (null != _rdr)
                    {
                        _rdr.debugln("Caught exception in ThreadPoolCallBack: " + ex.ToString());
                    }
                }
                finally
                {
                lock (_rdr._backgroundNotifierLock)
                    _rdr._backgroundNotifierCallbackCount--;

                releaseCallBackMethod.Set();
            }
            }

            #endregion
        }

        #endregion

        #region Settings

        /// <summary>
        /// Parameter object
        /// </summary>
        protected class Setting
        {
            #region Fields

            // Name of parameter.
            // Store here because dispatch table only keeps case-insensitive version.
            internal string Name;

            // Data type
            internal Type Type;

            // Data value
            internal Object Value;

            // Write permission -- can parameter be set?
            internal bool Writeable;

            //if the setting is Confirmed to exist
            internal bool Confirmed;

            // store the value of the parameter in paramGet
            internal bool CacheGetValue;

            // Filters for get and set actions.
            // Convert Object types, call appropriate hooks on data going in or out of the Setting.
            internal SettingFilter GetFilter;
            internal SettingFilter SetFilter;

            #endregion

            #region Construction

            /// <summary>
            /// Create a parameter object, which includes parameter value and metadata.
            /// </summary>
            /// <param name="name">Name of parameter</param>
            /// <param name="type">Data type; e.g., typeof(int)</param>
            /// <param name="value">Stored value</param>
            /// <param name="writeable">Allow write access?</param>
            public Setting(string name, Type type, Object value, bool writeable) : this(name, type, value, writeable, null, null) { }

            /// <summary>
            /// Create a parameter object, which includes parameter value and metadata.
            /// </summary>
            /// <param name="name">Name of parameter</param>
            /// <param name="type">Data type; e.g., typeof(int)</param>
            /// <param name="value">Stored value</param>
            /// <param name="writeable">Allow write access?</param>
            /// <param name="getfilter">Filter to use on ParamGet.  NOTE: If value is mutable, always make a copy in getfilter to prevent unintentional modifications.</param>
            /// <param name="setfilter">Filter to use on ParamSet.</param>
            public Setting(string name, Type type, Object value, bool writeable, SettingFilter getfilter, SettingFilter setfilter) : this(name, type, value, writeable, getfilter, setfilter, true) { }

            /// <summary>
            /// Create a parameter object, which includes parameter value and metadata.
            /// </summary>
            /// <param name="name">Name of parameter</param>
            /// <param name="type">Data type; e.g., typeof(int)</param>
            /// <param name="value">Stored value</param>
            /// <param name="writeable">Allow write access?</param>
            /// <param name="getfilter">Filter to use on ParamGet.  NOTE: If value is mutable, always make a copy in getfilter to prevent unintentional modifications.</param>
            /// <param name="setfilter">Filter to use on ParamSet.</param>
            /// <param name="confirmed">If the parameter is Confirmed </param>
            public Setting(string name, Type type, Object value, bool writeable, SettingFilter getfilter, SettingFilter setfilter, bool confirmed) : this(name, type, value, writeable, getfilter, setfilter, true, false) { }

            /// <summary>
            /// Create a parameter object, which includes parameter value and metadata.
            /// </summary>
            /// <param name="name">Name of parameter</param>
            /// <param name="type">Data type; e.g., typeof(int)</param>
            /// <param name="value">Stored value</param>
            /// <param name="writeable">Allow write access?</param>
            /// <param name="getfilter">Filter to use on ParamGet.  NOTE: If value is mutable, always make a copy in getfilter to prevent unintentional modifications.</param>
            /// <param name="setfilter">Filter to use on ParamSet.</param>
            /// <param name="confirmed">If the parameter is Confirmed </param>
            /// <param name="cacheGetValue">store the value of the parameter in paramGet</param>
            public Setting(string name, Type type, Object value, bool writeable, SettingFilter getfilter, SettingFilter setfilter,bool confirmed,bool cacheGetValue)
            {
                if (value != null && type.IsInstanceOfType(value) == false)
                {
                    throw new ArgumentException("Wrong type for parameter initial value.");
                }

                Name = name;
                Type = type;
                Value = value;
                Writeable = writeable;
                GetFilter = getfilter;
                SetFilter = setfilter;
                Confirmed = confirmed;
                CacheGetValue = cacheGetValue;
            }

            #endregion
        }

        #endregion

        #region ReadCollector

        private sealed class ReadCollector
        {
            #region Fields

            public List<TagReadData> ReadList = new List<TagReadData>(); 

            #endregion

            #region Cosntruction

            public void HandleRead( object sender, TagReadDataEventArgs e )
            {
                this.ReadList.Add( e.TagReadData );
            }

            #endregion
        }

        #endregion

        #region Reader Stats
        
        /// <summary>
        /// Reader stats object.
        /// </summary>
        public class Stat
        {
            private StatsFlag resetReaderStats = StatsFlag.ALL;
            private StatsFlag resetM3eReaderStats = StatsFlag.TEMPERATURE | StatsFlag.DCVOLTAGE;

            /// <summary>
            /// Reset reader stats
            /// </summary>
            public StatsFlag RESETREADERSTATS
            {
                get { return resetReaderStats; }
            }

            /// <summary>
            /// Reset M3e reader stats
            /// </summary>
            public StatsFlag RESETM3EREADERSTATS
            {
                get { return resetM3eReaderStats; }
            }

            #region PerAntennaValues

            /// <summary>
            /// Per Antenna stats
            /// </summary>
            public class PerAntennaValues
            {
                #region Fields

                /// <summary>
                /// Antenna ID
                /// </summary>
                public UInt16 Antenna = 0;
                /// <summary>
                /// Current RF on time (since start of search) (milliseconds)
                /// </summary>
                public UInt32 RfOnTime = 0;
                /// <summary>
                /// Noise Floor (TX on, all connected antennas) (dBm)
                /// </summary>
                public SByte NoiseFloor = 0;

                #endregion
            }

            #endregion PerAntennaValues

            #region ReaderStatsValues
            
            /// <summary>
            /// Reader stats values
            /// </summary>
            public class Values : Stat
            {
                /// <summary>
                /// Cache stats flag
                /// </summary>
                private StatsFlag valid = StatsFlag.NONE;
                /// <summary>
                /// Current temperature (degrees C)
                /// </summary>
                private SByte? temperature = null;
                /// <summary>
                /// Current tag protocol
                /// </summary>
                private TagProtocol protocol = TagProtocol.NONE;
                /// <summary>
                /// Current antenna
                /// </summary>
                private UInt16? antenna = null;
                /// <summary>
                /// Current RF carrier frequency (KHZ)
                /// </summary>
                private UInt32? frequency = null;
                /// <summary>
                /// Current connected antennas
                /// </summary>
                private uint[] connectedAntennas = null;
                /// <summary>
                /// Per-antenna values
                /// </summary>
                private List<PerAntennaValues> perAntenna = null;
                /// <summary>
                /// Total Number of Antenna Available
                /// </summary>
                internal int totalAntennaCount = 0;
                /// <summary>
                /// Current dc voltage
                /// </summary>
                private UInt16? dcVoltage = null;

                /// <summary>
                /// Cache stats flag
                /// </summary>
                public StatsFlag VALID
                {
                    get { return valid; }
                    set { valid = value; }
                }
                /// <summary>
                /// Current temperature (degrees C)
                /// </summary>
                public SByte? TEMPERATURE
                {
                    get { return temperature; }
                    set { temperature = value; }
                }
                /// <summary>
                /// Current tag protocol
                /// </summary>
                public TagProtocol PROTOCOL
                {
                    get { return protocol; }
                    set { protocol = value; }
                }
                /// <summary>
                /// Current antenna
                /// </summary>
                public UInt16? ANTENNA
                {
                    get { return antenna; }
                    set { antenna = value; }
                }
                /// <summary>
                /// Current RF carrier frequency (KHZ)
                /// </summary>
                public UInt32? FREQUENCY
                {
                    get { return frequency; }
                    set { frequency = value; }
                }
                /// <summary>
                /// Current connected antennas
                /// </summary>
                public uint[] CONNECTEDANTENNA
                {
                    get { return connectedAntennas; }
                    set { connectedAntennas = value; }
                }
                /// <summary>
                /// Per-antenna values
                /// </summary>
                public List<PerAntennaValues> PERANTENNA
                {
                    get { return perAntenna; }
                    set { perAntenna = value; }
                }

                /// <summary>
                /// Current dc voltage
                /// </summary>
                public UInt16? DCVOLTAGE
                {
                    get { return dcVoltage; }
                    set { dcVoltage = value; }
                }

                #region ToString
                /// <summary>
                /// Human-readable representation
                /// </summary>
                /// <returns>Human-readable representation</returns>
                public override string ToString()
                {
                        StringBuilder stringbuild = new StringBuilder();
                        if (0 != (valid & Stat.StatsFlag.CONNECTEDANTENNAS))
                        {
                            stringbuild.Append("\n\n" + "Antenna Connection Status" + "\n" );
                            List<uint> connectedAnts = new List<uint>();
                            connectedAnts.AddRange(connectedAntennas);
                            if (null != connectedAntennas)
                            {
                                for (uint i = 1; i <= totalAntennaCount; i++ )
                                {
                                    if (connectedAnts.Contains(i))
                                        stringbuild.Append(" Antenna " + i.ToString() + " | " + " Connected" + "\n");
                                    else
                                        stringbuild.Append(" Antenna " + i.ToString() + " | " + " Disconnected" + "\n");
                                    
                                }
                            }
                        }
                        if (0 != (valid & Stat.StatsFlag.NOISEFLOORSEARCHRXTXWITHTXON))
                        {
                            stringbuild.Append("\n\n" + "Noise Floor With TX ON" + "\n");
                            if (null != perAntenna)
                            {
                                foreach (PerAntennaValues b in perAntenna)
                                {
                                    stringbuild.Append(" Antenna " + b.Antenna.ToString() + " | "
                                        + b.NoiseFloor.ToString() + " dBm" + "\n");
                                }
                            }
                        }
                        if (0 != (valid & Stat.StatsFlag.RFONTIME))
                        {
                            stringbuild.Append("\n\n" + "RF ON Time" + "\n");
                            if (null != perAntenna)
                            {
                                foreach (PerAntennaValues b in perAntenna)
                                {
                                    stringbuild.Append(" Antenna " + b.Antenna.ToString() + " | "
                                        + b.RfOnTime.ToString() + " ms"+"\n");
                                }
                            }
                        }
                        if (0 != (valid & Stat.StatsFlag.FREQUENCY))
                        {
                            stringbuild.Append("\n\n" + "Frequency: " + frequency.ToString() + " kHz" +"\n");
                        }
                        if (0 != (valid & Stat.StatsFlag.TEMPERATURE))
                        {
                            stringbuild.Append("Temperature: " + temperature.ToString() + " C"+"\n");
                        }
                        if (0 != (valid & Stat.StatsFlag.PROTOCOL))
                        {
                            stringbuild.Append("Protocol: " + protocol.ToString()+"\n");
                        }
                        if (0 != (valid & Stat.StatsFlag.ANTENNAPORTS))
                        {
                            stringbuild.Append("Current Antenna: " + antenna.ToString()+"\n");
                        }
                        if (0 != (valid & Stat.StatsFlag.DCVOLTAGE))
                        {
                            stringbuild.Append("Current dc Voltage(mV): " + dcVoltage.ToString() + "\n");
                        }
                       
                    return stringbuild.ToString();
                }
                #endregion ToString
            }

            #endregion ReaderStatsValues

            #region ReaderStatsFlag

            /// <summary>
            /// Reader Stats Flag Enum - 
            /// </summary>
            [Flags]
            public enum StatsFlag
            {
                /// <summary>
                /// None
                /// </summary>
                NONE = 0,
                /// <summary>
                /// Total time the port has been transmitting, in milliseconds. Resettable
                /// </summary>
                RFONTIME = 1 << 0,
                /// <summary>
                /// Noise floor with the TX on for the antennas were last configured for searching
                /// </summary>
                NOISEFLOORSEARCHRXTXWITHTXON = 1 << 6,
                /// <summary>
                /// Current frequency in units of KHz
                /// </summary>
                FREQUENCY = 1 << 7,
                /// <summary>
                /// Current temperature of the device in units of Celsius
                /// </summary>
                TEMPERATURE = 1 << 8,
                /// <summary>
                /// Current antenna
                /// </summary>
                ANTENNAPORTS = 1 << 9,
                /// <summary>
                /// Current protocol
                /// </summary>
                PROTOCOL = 1 << 10,
                /// <summary>
                /// Current connected antennas
                /// </summary>
                CONNECTEDANTENNAS = 1 << 11,
                /// <summary>
                /// Current DC Voltage
                /// </summary>
                DCVOLTAGE = 1 << 14,
                /// <summary>
                /// ALL
                /// </summary>
                ALL = (RFONTIME |
                       NOISEFLOORSEARCHRXTXWITHTXON |
                       TEMPERATURE |
                       PROTOCOL |
                       ANTENNAPORTS |
                       FREQUENCY |
                       CONNECTEDANTENNAS),
            }

            #endregion ReaderStatsFlag
        }

        #endregion Reader Stats

        #region LicenseOption
        /// <summary>
        /// Operation Options for CmdSetProtocolLicenseKey 
        /// </summary>
        public enum LicenseOption
        {
            /// <summary>
            /// Set valid license key
            /// </summary>
            SET_LICENSE_KEY = 0x01,

            /// <summary>
            /// Erase license key
            /// </summary>
            ERASE_LICENSE_KEY = 0x02,

        }

        #endregion

        #region LicenseOperation

        /// <summary>
        /// Class for License key operation
        /// </summary>
        public class LicenseOperation
        {
            #region Fields

            /// <summary>
            /// option for license operation
            /// </summary>
            public LicenseOption option;

            /// <summary>
            /// license key for setting license to the module
            /// </summary>
            public byte[] key;

            /// <summary>
            /// license operation
            /// </summary>
            public LicenseOperation()
            {

            }

            #endregion
        }
        #endregion

        #endregion Nested Classes

        #region Fields

        /// <summary>
        /// Track doneness of ThreadPool callbacks
        /// </summary>
        internal int    _backgroundNotifierCallbackCount = 0;
        private Object _backgroundNotifierLock = new Object();

        private Dictionary<string, Setting> _params = null;
        /// <summary>
        /// Creates  a Reader factory method.
        /// </summary>
	    public delegate Reader ReaderFactory(string uriString);

        /// <summary>
        /// Creates  a Dictionary with string and object as key value pair.
        /// </summary>
        private static Dictionary<string, ReaderFactory> ReaderFactoryDispatchTable = new Dictionary<string, ReaderFactory>();
       
        /// <summary>
        /// Queue for BackGroundReader and BackGroundNotifier
        /// </summary>
        private  Queue<TagReadData> tagReadQueue = new Queue<TagReadData>();
        private Queue<ReaderStatsReport> RFSurvyQueue = new Queue<ReaderStatsReport>();
       
        /// <summary>
        /// Internal flag to enable "tag reading."
        /// If true, generate tag reads.  If false, stop "reading tags."
        /// </summary>
        protected bool _runNow = false;
        /// <summary>
        /// Internal flag to Check "tag reading type."
        /// If true, pseudo async read enabled."
        /// </summary>
        protected bool _isPseudoAsyncRead = false;
        /// <summary>
        /// Internal flag to "close reader."
        /// If true, quit worker thread.
        /// </summary>
        protected bool _exitNow = false;

        /// <summary>
        ///Get the time elapsed for processing the tagread data
        /// </summary>
        protected DateTime timeStart, timeEnd;

        /// <summary>
        ///string to append connect time network reader error messages
        /// </summary>
        public static string connectionErrors = string.Empty;

        /// <summary>
        /// In case user specified the timeout value for connect
        /// Enable the userTransportTimeoutEnable option
        /// </summary>
        protected bool userTransportTimeoutEnable = false;

        private Thread _workerThread = null;

        Uri uri;

        /// <summary>
        /// Fast search enable 
        /// </summary>
        protected bool isFastSearch;

        /// <summary>
        /// To enable module duty cycle
        /// </summary>
        protected bool isDutyCycleFlag = false;
        
        /// <summary>
        /// Cache reader stats flag
        /// </summary>
        protected Reader.Stat.StatsFlag statFlag = Stat.StatsFlag.NONE;

        /// <summary>
        /// Cache reader stats flag
        /// </summary>
        public Reader.Stat.StatsFlag _StatFlag
        {
            get { return statFlag; }
            set { statFlag = value; }
        }
        
        /// <summary>
        /// Flag to show read status during Read stop trigger
        /// </summary>
        protected Boolean finishedReading = false;

        #endregion

        #region Properties

        private Dictionary<string, Setting> Params
        {
            get
            {
                if (null == _params)
                {
                    _params = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);

                    ParamAdd(new Setting("/reader/read/asyncOnTime", typeof(int), 250, true, delegate(Object val)
                {
                    return val;
                },
                delegate(Object val)
                {
                    if ((int)val < 0)
                    {
                        throw new ArgumentOutOfRangeException("Negative value for asyncOnTime Not Supported");
                    }
                    return val;
                }));
                    ParamAdd(new Setting("/reader/read/asyncOffTime", typeof(int), 0, true, delegate(Object val)
                {
                    return val;
                },
                delegate(Object val)
                {
                    if ((int)val < 0)
                    {
                        throw new ArgumentOutOfRangeException("Negative value for asyncOffTime Not Supported");
                    }
                    return val;
                }));
                    ParamAdd(new Setting("/reader/gen2/accessPassword", typeof(Gen2.Password), new Gen2.Password(0), true,
                        null,
                        delegate(Object val)
                        {
                            if (null == val)
                                val = new Gen2.Password(0);
                            return val;
                        }
                        ));
                   // ParamAdd(new Setting("/reader/read/filter", typeof(TagFilter), null, true, null, null));                    
                    ParamAdd(new Setting("/reader/uri", typeof(string), null,false,
                            delegate(Object val)
                            {
                                val = uri.ToString();
                                return val;
                            },
                            null));
                    ParamAdd(new Setting("/reader/transportTimeout", typeof(int), 5000, true, delegate(Object val)
                        {
                            return val;
                        },
                        delegate(Object val)
                        {
                            if ((int)val < 0)
                            {
                                throw new ArgumentOutOfRangeException("Negative Timeout Not Supported");
                            }
                            // In case user specified the timeout value for connect
                            // Enable the userTransportTimeoutEnable option
                            userTransportTimeoutEnable = true;
                            return val;
                        }));
                    ParamAdd(new Setting("/reader/commandTimeout", typeof(int), 1000, true, delegate(Object val)
                        {
                            return val;
                        },
                        delegate(Object val)
                        {
                            if ((int)val < 0)
                            {
                                throw new ArgumentOutOfRangeException("Negative Timeout Not Supported");
                            }
                            return val;
                        }));
                }

                return _params;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when each tag is read.
        /// </summary>
        public event EventHandler< TagReadDataEventArgs > TagRead;        

        /// <summary>
        /// Transport message was sent or received
        /// </summary>
        public event EventHandler< TransportListenerEventArgs > Transport;

        /// <summary>
        /// Occurs when asynchronous read throws an exception.
        /// </summary>
        public event EventHandler< ReaderExceptionEventArgs > ReadException;

        /// <summary>
        /// Occurs when reader status parsing in continuous read 
        /// </summary>
        public  event EventHandler<StatusReportEventArgs> StatusListener;

        /// <summary>
        /// Occurs when reader status parsing in continuous read 
        /// </summary>
        public event EventHandler<StatsReportEventArgs> StatsListener;

        /// <summary>
        /// Occurs when 0x604 error is received which indicates the api 
        /// is waiting for the client to provide the accesspassword of tag.
        /// </summary>
        public event EventHandler<ReadAuthenticationEventArgs> ReadAuthentication;

        /// <summary>
        /// Target pattern for debug log messages
        /// </summary>
        /// <param name="message"></param>
        public delegate void LogHandler(string message);
        /// <summary>
        /// Occurs when debug log message is generated
        /// </summary>
        public event LogHandler Log;
        /// <summary>
        /// Generate a debug log message
        /// </summary>
        /// <param name="message">log message content</param>
        protected void OnLog(string message)
        {
            if (null != Log) { Log(message); }
        }

        #endregion

        #region Abstract Methods

        #region Connect

        /// <summary>
        /// Connect reader object to device.
        /// If object already connected, then do nothing.
        /// </summary>
        public abstract void Connect();

        #endregion

        #region ReceiveAutonomousReading
        /// <summary>
        /// Receives data from module continuously
        /// </summary>
        public abstract void ReceiveAutonomousReading();

        #endregion

        #region Reboot
        /// <summary>
        /// Reboots the device
        /// </summary>
        public abstract void Reboot();
        #endregion

        #region LoadConfig

        /// <summary>
        /// Loads the reader configuration parameters from file and applies to module
        /// </summary>
        /// <param name="filePath">load reader configurations from filepath</param>
        public void LoadConfig(string filePath)
        {
            LoadSaveConfiguration loadConfig = new LoadSaveConfiguration();
            loadConfig.LoadConfiguration(filePath, this, false);
        }

        #endregion

        #region SaveConfig

        /// <summary>
        /// Saves the current reader configuration parameters and its values to a file
        /// </summary>
        /// <param name="filePath">filepath to save reader confuguratons</param>
        public void SaveConfig(string filePath)
        {
            string[] parametersList = this.ParamList();
            List<string> ReadOnlyPararameters = new List<string>();
            foreach (string key in parametersList)
            {
                switch (key)
                {
                    case "/reader/licenseKey":
                    case "/reader/stats":
                    case "/reader/userConfig":
                    case "/reader/statistics":
                    case "/reader/region/lbt/enable":
                    case "/reader/region/lbtThreshold":
                    case "/reader/region/dwellTime/enable":
                    case "/reader/region/dwellTime":
                    case "/reader/radio/enablePowerSave":
                    case "/reader/gen2/bap":
                    case "/reader/iso180006b/modulationDepth":
                    case "/reader/iso180006b/delimiter":
                    case "/reader/iso180006b/BLF":
                    case "/reader/radio/enableSJC":
                    case "/reader/userMode":
                    case "/reader/gen2/writeMode":
                    case "/reader/gen2/writeEarlyExit":
                    case "/reader/gen2/writeReplyTimeout":
                    case "/reader/description":
                    case "/reader/hostname":
                    case "/reader/tagop/antenna":
                    case "/reader/regulatory/enable":
                    case "/reader/region/minimumFrequency":
                    case "/reader/region/quantizationStep":
                    case "/reader/manageLicenseKey":
                    case "/reader/metadata":
                    case"/reader/antenna/perAntennaTime":
                    case "/reader/stats/enable":
                        ReadOnlyPararameters.Add(key);
                        continue;
                }
                if (key == "/reader/antenna/settlingTimeList")
                {
                    continue;
                }
                Setting set = ValidateParameterKey(key);
                if (false == set.Writeable)
                    ReadOnlyPararameters.Add(key);
            }
            LoadSaveConfiguration saveConfig = new LoadSaveConfiguration();
            saveConfig.ReadOnlyPararameters = ReadOnlyPararameters;
            saveConfig.SaveConfiguration(filePath, this);
        }

        #endregion

        #region Destroy

        /// <summary>
        /// Shuts down the connection with the reader device.
        /// </summary>
        public abstract void Destroy();

        #endregion

        #region Read

        /// <summary>
        /// Read RFID tags for a fixed duration.
        /// </summary>
        /// <param name="timeout">the time to spend reading tags, in milliseconds</param>
        /// <returns>the tags read</returns>
        public abstract TagReadData[] Read(int timeout);

        #endregion

        #region StartReading

        /// <summary>
        /// Start reading RFID tags in the background. The tags found will be
        /// passed to the registered read listeners, and any exceptions that
        /// occur during reading will be passed to the registered exception
        /// listeners. Reading will continue until stopReading() is called.
        /// </summary>
        public abstract void StartReading();

        #endregion

        #region StopReading

        /// <summary>
        /// Stop reading RFID tags in the background.
        /// </summary>
        public abstract void StopReading();

        #endregion

        #region Read status
        
        /// <summary>
        /// This function will check for Stop reading status and will return true once the reader has stopped reading.
        /// </summary>
        /// <returns></returns>
        public Boolean isReadStopped()
        {
            // This flag will be true only after receiving Stop Read response.
            return finishedReading;
        }
        #endregion

        #region FirmwareLoad

        /// <summary>
        /// Load a new firmware image into the device's nonvolatile memory.
        /// This installs the given image data onto the device and restarts
        /// it with that image. The firmware must be of an appropriate type
        /// for the device. Interrupting this operation may damage the
        /// reader.
        /// </summary>
        /// <param name="firmware">a data _stream of the firmware contents</param>
        public abstract void FirmwareLoad(System.IO.Stream firmware);


        /// <summary>
        /// Load a new firmware image into the device's nonvolatile memory.
        /// This installs the given image data onto the device and restarts
        /// it with that image. The firmware must be of an appropriate type
        /// for the device. Interrupting this operation may damage the
        /// reader.
        /// </summary>
        /// <param name="firmware">a data _stream of the firmware contents</param>
        /// <param name="flOptions">firmware load options</param>
        public abstract void FirmwareLoad(System.IO.Stream firmware, FirmwareLoadOptions flOptions);

        #endregion

        #region GpiGet

        /// <summary>
        /// Get the state of all of the reader's GPI pins. 
        /// </summary>
        /// <returns>array of GpioPin objects representing the state of all input pins</returns>
        public abstract GpioPin[] GpiGet();

        #endregion

        #region GpoSet

        /// <summary>
        /// Set the state of some GPO pins.
        /// </summary>
        /// <param name="state">array of GpioPin objects</param>
        public abstract void GpoSet(ICollection<GpioPin> state);

        #endregion

        #region ExecuteTagOp

        /// <summary>
        /// execute a TagOp
        /// </summary>
        /// <param name="tagOP">Tag Operation</param>
        /// <param name="target">Tag filter</param>
        ///<returns>the return value of the tagOp method if available</returns>

        public abstract Object ExecuteTagOp(TagOp tagOP, TagFilter target);

        #endregion

        #region KillTag

        /// <summary>
        /// Kill a tag. The first tag seen is killed.
        /// </summary>
        /// <param name="target">the tag to kill, or null</param>
        /// <param name="password">the authentication needed to kill the tag</param>
        public abstract void KillTag(TagFilter target, TagAuthentication password);

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
        public abstract void LockTag(TagFilter target, TagLockAction action);

        #endregion

        #region ReadTagMemBytes

        /// <summary>
        /// Read data from the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to read from, or null</param>
        /// <param name="bank">the tag memory bank to read from</param>
        /// <param name="byteAddress">the word address to start reading from</param>
        /// <param name="byteCount">the number of words to read</param>
        /// <returns>the words read</returns>
        public abstract byte[] ReadTagMemBytes(TagFilter target, int bank, int byteAddress, int byteCount);

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
        public abstract ushort[] ReadTagMemWords(TagFilter target, int bank, int wordAddress, int wordCount);

        #endregion

        #region WriteTag

        /// <summary>
        /// Write a new ID to a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="epc">the new tag ID to write</param>
        public abstract void WriteTag(TagFilter target, TagData epc);

        #endregion

        #region WriteTagMemBytes

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="bank">the tag memory bank to write to</param>
        /// <param name="address">the byte address to start writing to</param>
        /// <param name="data">the bytes to write</param>
        public abstract void WriteTagMemBytes(TagFilter target, int bank, int address, ICollection<byte> data);

        #endregion

        #region WriteTagMemWords

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag to write to, or null</param>
        /// <param name="bank">the tag memory bank to write to</param>
        /// <param name="address">the word address to start writing to</param>
        /// <param name="data">the words to write</param>
        public abstract void WriteTagMemWords(TagFilter target, int bank, int address, ICollection<ushort> data);

        #endregion

        ///// <summary>
        ///// Custom region Configuration
        ///// </summary>
        ///// <param name="LBTEnable"></param>
        ///// <param name="LBTThreshold"></param>
        ///// <param name="dwellTimeEnable"></param>
        ///// <param name="dwellTime"></param>
        //public abstract void regionConfiguration(bool LBTEnable, Int16 LBTThreshold, bool dwellTimeEnable, UInt16 dwellTime);
        #endregion

        #region SetSerialTransport

        /// <summary>
        ///Creates a serial transport dispatch table
        /// </summary>
        public static void SetSerialTransport(string scheme, ReaderFactory factory)
        {
            if (scheme.Equals("llrp") || scheme.Equals("rql"))
            {
                throw new Exception("Unsupported SerialTransport scheme");
            }
            if (ReaderFactoryDispatchTable.Count == 0)
            {
                ReaderFactoryDispatchTable.Add("tmr", SerialTransportNative.CreateSerialReader);
                ReaderFactoryDispatchTable.Add("eapi", SerialTransportNative.CreateSerialReader);
            }
            if (ReaderFactoryDispatchTable.ContainsKey(scheme))
            {
                ReaderFactoryDispatchTable[scheme] = factory;
            }
            else
            {
                ReaderFactoryDispatchTable.Add(scheme, factory);
            }
        }
        
        #endregion

        #region Create

        /// <summary>
        /// Return an instance of a Reader class associated with a
        /// serial reader on a particular communication port.
        /// </summary>
        /// <param name="uriString">Identifies the reader to connect to with a URI
        /// syntax. The scheme can be "eapi" for the embedded module
        /// protocol, "rql" for the request query language, or "tmr" to
        /// guess. The remainder of the string identifies the _stream that the
        /// protocol will be spoken over, either a local host serial port
        /// device or a TCP network port.
        /// Examples include: 
        ///   "eapi:///dev/ttyUSB0"
        ///   "eapi:///com1"
        ///   "eapi://modproxy.example.com:2500/"
        ///   "rql://reader.example.com/"
        ///   "tmr:///dev/ttyS0"
        ///   "tmr://192.168.1.101:80/"
        /// </param>
        /// <remarks>Set autoConnect to false if you need to reconfigure the reader object before opening any physical interfaces
        /// (e.g., attach a transport listener to monitor the init sequence, set a nonstandard baud rate or transport timeout.)
        /// If autoConnect is false, Create will just create the reader object, which may then be configured
        /// before the actual connection is made by calling its Connect method.</remarks>
        /// <returns>Reader object associated with device</returns>
        public static Reader Create(string uriString)
        {
            Uri objUri;
            Reader reader;

            try
            {
                objUri = new Uri(uriString);
            }
            catch (UriFormatException e)
            {
                throw new ReaderException(e.Message);
            }

            String scheme = objUri.Scheme;

            if (scheme == null)
            {
                throw new ReaderException("Blank URI scheme");
            }
            // checking dispatchtable empty or not
            bool isEmpty = (ReaderFactoryDispatchTable.Count == 0);
            // Dispatchtable is empty add the standard uri schemes
            if (isEmpty)
            {
                ReaderFactoryDispatchTable.Add("tmr", SerialTransportNative.CreateSerialReader);
                ReaderFactoryDispatchTable.Add("eapi", SerialTransportNative.CreateSerialReader);
            }
               
#if !WindowsCE
                if (scheme.Equals("tmr"))
                {
                    if (objUri.Host != null && objUri.Host != "")
                    {
                        reader = new LlrpReader(objUri.Host, objUri.Port);
                        if (((LlrpReader)reader).IsLlrpReader())
                        {
                            reader.uri = objUri;
                            return reader;
                        }
                        else
                        {
                            reader.Destroy();
                            reader = null;
                            scheme = "rql";
                        }
                    }
                    else
                    {
                        scheme = "eapi";
                    }
                }
#endif
                //If DispatchTable contains the scheme assign equivalent transport object
                if (ReaderFactoryDispatchTable.ContainsKey(scheme))
                {
                    reader = ReaderFactoryDispatchTable[scheme](uriString);
                }
                else
                {
                    switch (scheme)
                    {
                        case "rql":

                            if ("" == objUri.Host)
                                throw new ArgumentException("Must provide a host name for URI (rql://hostname)");

                            if (!(("" == objUri.PathAndQuery) || ("/" == objUri.PathAndQuery)))
                                throw new ArgumentException("Path not supported for " + objUri.Scheme + " URIs");

                            reader = new RqlReader(objUri.Host, objUri.Port);
                            break;
#if !WindowsCE
                        case "llrp":

                            if ("" == objUri.Host)
                                throw new ArgumentException("Must provide a host name for URI (llrp://hostname)");

                            if (!(("" == objUri.PathAndQuery) || ("/" == objUri.PathAndQuery)))
                                throw new ArgumentException("Path not supported for " + objUri.Scheme + " URIs");

                            reader = new LlrpReader(objUri.Host, objUri.Port);

                            break;
#endif
                        default:
                            throw new ReaderException("Unknown URI scheme: " + objUri.Scheme + " in " + uriString);
                    }
                }
            reader.uri = objUri;
            return reader;
        }

        #endregion

        #region Dispose

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="bDisposing">is Disposing?</param>
        /// <returns>void</returns>
        protected override void Dispose(bool bDisposing)
        {
            Destroy();
            base.Dispose(bDisposing);
        }

        #endregion

        #region Setting Methods

        #region ValidateParameterKey
        /// <summary>
        /// Check for existence of parameter.  Throw exception if parameter does not exist.
        /// </summary>
        /// <param name="key">Parameter name</param>
        /// <returns>Setting if key is valid.  Otherwise, throws ArgumentException.</returns>
        protected Setting ValidateParameterKey(string key)
        {
            if (false == Params.ContainsKey(key))
                throw new ArgumentException("Parameter not found: \"" + key + "\"");
            else
                return Params[key];
        }

        #endregion

        #region ParamAdd
        /// <summary>
        /// Register a new parameter handler
        /// </summary>
        /// <param name="handler">Parameter handler.
        /// Get method will be called -- parameter will only be added if get succeeds.</param>
        protected void ParamAdd(Setting handler)
        {
            // IMPORTANT!  Put params in dictionary using item interface (dict[key]=value)
            // instead of dict.Add(key,value) method to avoid exceptions when overwriting
            // parameter definitions (e.g., after firmware update.)
            // http://msdn.microsoft.com/en-us/library/k7z0zy8k(VS.80).aspx
            Params[handler.Name] = handler;
        }

        #endregion

        #region ParamClear

        /// <summary>
        ///  Reset parameter table; e.g., to reprobe hardware afer firmware update
        /// </summary>
        protected void ParamClear()
        {
            _params = null;
        }

        #endregion

        #region probeSetting

        /// <summary>
        /// Probe if the parameter exists in the module.
        /// </summary>
        /// <param name="s">the parameter setting</param>
        /// <returns>the boolean value representing the existence of the parameter in the module</returns>
        bool probeSetting(Setting s)
        {
            try
            {
                s.GetFilter(null);
                s.Confirmed = true;
            }
            catch (ReaderException)
            {
            }
            if (s.Confirmed == false)
            {
                Params.Remove(s.Name);
            }
            return s.Confirmed;

        }

        #endregion

        #region ParamGet

        /// <summary>
        /// Get the value of a Reader parameter.
        /// </summary>
        /// <param name="key">the parameter name</param>
        /// <returns>the value of the parameter, as an Object</returns>
        public Object ParamGet(string key)
        {
            Setting set = ValidateParameterKey(key);
            Object  val = set.Value;

            if (set.Confirmed==false && probeSetting(set)== false)
            {
                throw new ArgumentException("Parameter not found: \"" + key + "\"");
            }

            if (null != set.GetFilter)
                val = set.GetFilter(val);

            else
            {
                // Don't ever return a direct reference to a mutable value
                if (val is ICloneable)
                    val = ((ICloneable) val).Clone();
            }
            if (set.CacheGetValue)
            {
                set.Value = val;
            }
            return val;
        }

        #endregion

        #region ParamList

        /// <summary>
        /// Get a list of the parameters available
        /// 
        /// <para>Supported Parameters:
        /// <list type="bullet">
        /// <item><term>/reader/antenna/checkPort</term></item>
        /// <item><term>/reader/antenna/checkport</term></item>
        /// <item><term>/reader/antenna/connectedPortList</term></item>
        /// <item><term>/reader/antenna/perAntennaTime</term></item>
        /// <item><term>/reader/antenna/portList</term></item>
        /// <item><term>/reader/antenna/portSwitchGpos</term></item>
        /// <item><term>/reader/antenna/portSwitchGpos not supported</term></item>
        /// <item><term>/reader/antenna/portswitchgpos</term></item>
        /// <item><term>/reader/antenna/returnLoss</term></item>
        /// <item><term>/reader/antenna/settlingTimeList</term></item>
        /// <item><term>/reader/antenna/settlingtimelist</term></item>
        /// <item><term>/reader/antenna/txRxMap</term></item>
        /// <item><term>/reader/antenna/txrxmap</term></item>
        /// <item><term>/reader/antennaMode</term></item>
        /// <item><term>/reader/baudRate</term></item>
        /// <item><term>/reader/commandTimeout</term></item>
        /// <item><term>/reader/currentTime</term></item>
        /// <item><term>/reader/description</term></item>
        /// <item><term>/reader/extendedEpc</term></item>
        /// <item><term>/reader/extendedepc</term></item>
        /// <item><term>/reader/gen2/BLF</term></item>
        /// <item><term>/reader/gen2/InitQ</term></item>
        /// <item><term>/reader/gen2/SendSelect</term></item>
        /// <item><term>/reader/gen2/T4</term></item>
        /// <item><term>/reader/gen2/accessPassword</term></item>
        /// <item><term>/reader/gen2/accesspassword</term></item>
        /// <item><term>/reader/gen2/bap</term></item>
        /// <item><term>/reader/gen2/blf</term></item>
        /// <item><term>/reader/gen2/initQ</term></item>
        /// <item><term>/reader/gen2/initq</term></item>
        /// <item><term>/reader/gen2/protocolExtension</term></item>
        /// <item><term>/reader/gen2/protocolextension</term></item>
        /// <item><term>/reader/gen2/q</term></item>
        /// <item><term>/reader/gen2/sendSelect</term></item>
        /// <item><term>/reader/gen2/sendselect</term></item>
        /// <item><term>/reader/gen2/session</term></item>
        /// <item><term>/reader/gen2/t4</term></item>
        /// <item><term>/reader/gen2/tagEncoding</term></item>
        /// <item><term>/reader/gen2/tagencoding</term></item>
        /// <item><term>/reader/gen2/target</term></item>
        /// <item><term>/reader/gen2/target not supported</term></item>
        /// <item><term>/reader/gen2/tari</term></item>
        /// <item><term>/reader/gen2/writeEarlyExit</term></item>
        /// <item><term>/reader/gen2/writeMode</term></item>
        /// <item><term>/reader/gen2/writeReplyTimeout</term></item>
        /// <item><term>/reader/gen2/writeearlyexit</term></item>
        /// <item><term>/reader/gen2/writemode</term></item>
        /// <item><term>/reader/gpio/inputList</term></item>
        /// <item><term>/reader/gpio/inputlist</term></item>
        /// <item><term>/reader/gpio/outputList</term></item>
        /// <item><term>/reader/gpio/outputlist</term></item>
        /// <item><term>/reader/hostname</term></item>
        /// <item><term>/reader/iso14443a/supportedTagFeatures</term></item>
        /// <item><term>/reader/iso14443a/supportedTagTypes</term></item>
        /// <item><term>/reader/iso14443a/tagType</term></item>
        /// <item><term>/reader/iso14443a/tagtype</term></item>
        /// <item><term>/reader/iso14443b/supportedTagTypes</term></item>
        /// <item><term>/reader/iso14443b/tagType</term></item>
        /// <item><term>/reader/iso14443b/tagtype</term></item>
        /// <item><term>/reader/iso15693/supportedTagFeatures</term></item>
        /// <item><term>/reader/iso15693/supportedTagTypes</term></item>
        /// <item><term>/reader/iso15693/tagType</term></item>
        /// <item><term>/reader/iso15693/tagtype</term></item>
        /// <item><term>/reader/iso180006b/BLF</term></item>
        /// <item><term>/reader/iso180006b/blf</term></item>
        /// <item><term>/reader/iso180006b/delimiter</term></item>
        /// <item><term>/reader/iso180006b/modulationDepth</term></item>
        /// <item><term>/reader/iso180006b/modulationdepth</term></item>
        /// <item><term>/reader/lf125khz/secureRdFormat</term></item>
        /// <item><term>/reader/lf125khz/supportedTagFeatures</term></item>
        /// <item><term>/reader/lf125khz/supportedTagTypes</term></item>
        /// <item><term>/reader/lf125khz/tagType</term></item>
        /// <item><term>/reader/lf125khz/tagtype</term></item>
        /// <item><term>/reader/lf134khz/supportedTagTypes</term></item>
        /// <item><term>/reader/lf134khz/tagType</term></item>
        /// <item><term>/reader/lf134khz/tagtype</term></item>
        /// <item><term>/reader/licenseKey</term></item>
        /// <item><term>/reader/manageLicenseKey</term></item>
        /// <item><term>/reader/metadata</term></item>
        /// <item><term>/reader/metadata\</term></item>
        /// <item><term>/reader/powerMode</term></item>
        /// <item><term>/reader/powermode</term></item>
        /// <item><term>/reader/probeBaudRates</term></item>
        /// <item><term>/reader/probebaudrates</term></item>
        /// <item><term>/reader/protocolList</term></item>
        /// <item><term>/reader/radio/enablePowerSave</term></item>
        /// <item><term>/reader/radio/enableSJC</term></item>
        /// <item><term>/reader/radio/enablepowersave</term></item>
        /// <item><term>/reader/radio/enablesjc</term></item>
        /// <item><term>/reader/radio/keepRFOn</term></item>
        /// <item><term>/reader/radio/portReadPowerList</term></item>
        /// <item><term>/reader/radio/portWritePowerList</term></item>
        /// <item><term>/reader/radio/portreadpowerlist</term></item>
        /// <item><term>/reader/radio/portwritepowerlist</term></item>
        /// <item><term>/reader/radio/powerMax</term></item>
        /// <item><term>/reader/radio/powerMin</term></item>
        /// <item><term>/reader/radio/readPower</term></item>
        /// <item><term>/reader/radio/temperature</term></item>
        /// <item><term>/reader/radio/writePower</term></item>
        /// <item><term>/reader/read/asyncOffTime</term></item>
        /// <item><term>/reader/read/asyncOnTime</term></item>
        /// <item><term>/reader/read/plan</term></item>
        /// <item><term>/reader/read/trigger/gpi</term></item>
        /// <item><term>/reader/region/dwellTime</term></item>
        /// <item><term>/reader/region/dwellTime/enable</term></item>
        /// <item><term>/reader/region/hopTable</term></item>
        /// <item><term>/reader/region/hopTime</term></item>
        /// <item><term>/reader/region/hoptable</term></item>
        /// <item><term>/reader/region/id</term></item>
        /// <item><term>/reader/region/lbt/enable</term></item>
        /// <item><term>/reader/region/lbtThreshold</term></item>
        /// <item><term>/reader/region/minimumFrequency</term></item>
        /// <item><term>/reader/region/quantizationStep</term></item>
        /// <item><term>/reader/region/supportedRegions</term></item>
        /// <item><term>/reader/regulatory/enable</term></item>
        /// <item><term>/reader/regulatory/mode</term></item>
        /// <item><term>/reader/regulatory/modulation</term></item>
        /// <item><term>/reader/regulatory/offTime</term></item>
        /// <item><term>/reader/regulatory/onTime</term></item>
        /// <item><term>/reader/statistics</term></item>
        /// <item><term>/reader/stats</term></item>
        /// <item><term>/reader/stats is not supported</term></item>
        /// <item><term>/reader/stats/enable</term></item>
        /// <item><term>/reader/stats/enable is not supported</term></item>
        /// <item><term>/reader/status/antennaEnable</term></item>
        /// <item><term>/reader/status/antennaenable</term></item>
        /// <item><term>/reader/status/frequencyEnable</term></item>
        /// <item><term>/reader/status/frequencyenable</term></item>
        /// <item><term>/reader/status/temperatureEnable</term></item>
        /// <item><term>/reader/status/temperatureenable</term></item>
        /// <item><term>/reader/tagReadData/enableReadFilter</term></item>
        /// <item><term>/reader/tagReadData/readFilterTimeout</term></item>
        /// <item><term>/reader/tagReadData/recordHighestRssi</term></item>
        /// <item><term>/reader/tagReadData/reportRssiInDbm</term></item>
        /// <item><term>/reader/tagReadData/tagopFailures</term></item>
        /// <item><term>/reader/tagReadData/tagopSuccesses</term></item>
        /// <item><term>/reader/tagReadData/uniqueByAntenna</term></item>
        /// <item><term>/reader/tagReadData/uniqueByData</term></item>
        /// <item><term>/reader/tagReadData/uniqueByProtocol</term></item>
        /// <item><term>/reader/tagop/antenna</term></item>
        /// <item><term>/reader/tagop/protocol</term></item>
        /// <item><term>/reader/tagreaddata/enablereadfilter</term></item>
        /// <item><term>/reader/tagreaddata/recordhighestrssi</term></item>
        /// <item><term>/reader/tagreaddata/reportrssiIndbm</term></item>
        /// <item><term>/reader/tagreaddata/reportrssiindbm</term></item>
        /// <item><term>/reader/tagreaddata/uniquebyantenna</term></item>
        /// <item><term>/reader/tagreaddata/uniquebydata</term></item>
        /// <item><term>/reader/tagreaddata/uniquebyprotocol</term></item>
        /// <item><term>/reader/transportTimeout</term></item>
        /// <item><term>/reader/uri</term></item>
        /// <item><term>/reader/userConfig</term></item>
        /// <item><term>/reader/userMode</term></item>
        /// <item><term>/reader/usermode</term></item>
        /// <item><term>/reader/version/hardware</term></item>
        /// <item><term>/reader/version/model</term></item>
        /// <item><term>/reader/version/productGroup</term></item>
        /// <item><term>/reader/version/productGroupID</term></item>
        /// <item><term>/reader/version/productID</term></item>
        /// <item><term>/reader/version/serial</term></item>
        /// <item><term>/reader/version/software</term></item>
        /// <item><term>/reader/version/supportedProtocols</term></item>
        /// </list>
        /// </para>
        /// </summary>
        /// <returns>an array of the parameter names</returns>
        public string[] ParamList()
        {
            List<string> names = new List<string>();

            foreach (Setting set in Params.Values)
            {
                if (set.Confirmed == false && probeSetting(set) == false)
                {
                    continue;
                }

                names.Add(set.Name);
            }

            //names.Sort();

            return names.ToArray();
        }

        #endregion

        #region ParamSet

        /// <summary>
        /// Set the value of a Reader parameter.
        /// </summary>
        /// <remarks>See <see>ParamGet</see> for list of supported parameters.</remarks>
        /// <param name="key">the parameter name</param>
        /// <param name="value">value of the parameter, as an Object</param>
        public void ParamSet(string key, Object value)
        {
            Setting set = ValidateParameterKey(key);


            if (set.Confirmed == false && probeSetting(set) == false)
            {
                throw new ArgumentException("Parameter not found: \"" + key + "\"");
            }

            if (false == set.Writeable)
                throw new ArgumentException("Parameter \"" + key + "\" is read-only.");

            if ((null != value) && (false == set.Type.IsAssignableFrom(value.GetType())))
                throw new ArgumentException("Wrong type " + value.GetType().Name + " for parameter \"" + key + "\".");

            Object val = value;

            if (null != set.SetFilter)
                val = set.SetFilter(val);

            set.Value = val;
        }

        #endregion

        #endregion

        #region Read Methods

        #region ReadGivenStartStop

        //// Implement Read, given working StartReading and StopReading methods

        /// <summary>
        /// Utility function to implement Read given working StartReading and StopReading methods
        /// </summary>
        /// <param name="milliseconds">Number of milliseconds to keep reading.</param>
        /// <returns>the read tag data collection</returns>
        protected TagReadData[] ReadGivenStartStop(int milliseconds)
        {
            ReadCollector collector = new ReadCollector();
            
            this.TagRead += new EventHandler< TagReadDataEventArgs >( collector.HandleRead );
            
            this.StartReading();
            
            Thread.Sleep(milliseconds);
            
            this.StopReading();
            
            this.TagRead -= new EventHandler< TagReadDataEventArgs >( collector.HandleRead );
            
            return collector.ReadList.ToArray();
        }

        #endregion

        #region StartReadingGivenRead

        //// Implement StartReading, given a working Read method

        /// <summary>
        /// Utility function to implement StartReading given a working Read method
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        protected void StartReadingGivenRead()
        {
            _isPseudoAsyncRead = true;
            _exitNow = false;
            _runNow  = true;
            if (null == _workerThread)
            {
                _workerThread = new Thread(DoWorkGivenRead);
                _workerThread.Name = "DoWorkGivenRead";
                _workerThread.IsBackground = true;
                _workerThread.Start();
            }
        }

        #endregion

        #region StopReadingGivenRead

        //// Implement StopReading, given a working Read method

        /// <summary>
        /// Utility function to implement StopReading given a working Read method
        /// </summary>
        protected void StopReadingGivenRead()
        {
            _isPseudoAsyncRead = false;
            _exitNow = true;
            _runNow = false;
            //Wait for all callbacks to be serviced
            int iteration = 0;
            while (0 < _backgroundNotifierCallbackCount)
            {
                iteration += 1;
                // Don't tie up the CPU.
                // Ideally, wait on the ThreadPool, but there's no accessor for that.
                debugln("Reader.StopReadingGivenRead iter#" + iteration + " CallBackCount=" + _backgroundNotifierCallbackCount);
                Thread.Sleep(100);
            }
            //The below if block was moved down so as to wait for all callbacks to be serviced
            if (null != _workerThread)
            {
                // Wait for read thread to finish
                _workerThread.Join();
                _workerThread = null;
            }
            }

        #endregion

        #region DoWorkGivenRead
        /// <summary>
        /// Logic for asynchronous worker thread given a working Read method
        /// </summary>
        protected void DoWorkGivenRead()
        {
            debugln("DEBUG Entered DoWorkGivenRead");
            try
            {
                int iteration = 0;
                while (false == _exitNow)
                {
                    iteration += 1;
                    debugln("DoWorkGivenRead iter#" + iteration);
                    debugln("_runNow = " + _runNow);
                    if (_runNow)
                    {
                        int readTime = (int)ParamGet("/reader/read/asyncOnTime");
                        int sleepTime = (int)ParamGet("/reader/read/asyncOffTime");

                        TagReadData[] reads = null;
                        try
                        {
                            fetchTagReads = true;
                            debugln("Calling read in DoWorkGivenRead");
                            reads = Read(readTime);
                            // Added the code to update the temperature during Pseudo Continuous Read and 
                            // continuous read when off time is not 0
                            // Don't throw a error if exception occurs while getting stats
                            try
                            {
                                Reader.Stat.Values objRdrStats = (Reader.Stat.Values)ParamGet("/reader/stats");
                                ReaderStatsReport statusreport = new ReaderStatsReport();
                                statusreport.STATS = objRdrStats;
                                OnStatsRead(statusreport);
                            }
                            catch (Exception)
                            { }
                            
                            debugln("Called read in DoWorkGivenRead");
                        }
                        catch (ArgumentException ae)
                        {
                            if (-1 != ae.Message.IndexOf("No valid antennas specified"))
                            {
                                if (this is SerialReader)
                                {
                                    SerialReader rdr = (SerialReader)this;
                                    // M5e and its variants hardware does not have a real PA protection. So doing the
                                    // read without antenna may cause the damage to the reader. Hence stopping the
                                    // read in this case. It's okay to let M6e and its variants continue to operate
                                    // because it has a PA protection mechanism.
                                    string model = (string)ParamGet("/reader/version/model");
                                    if ((model != "M6e") && (model != "M6e Micro") && (model != "M6e PRC")
                                        && (model != "M6e Micro USB") && (model != "M6e Micro USBPro") && (model != "M6e JIC"))
                                    {
                                        throw new ReaderException(ae.Message);
                                    }
                                    notifyExceptionListeners(new ReaderException(ae.Message));
                                }
                                else
                                {
                                    notifyExceptionListeners(new ReaderException(ae.Message));
                                }
                            }
                            else
                            {
                                throw ae;
                            }
                        }
                        if (!OffTimeAdded)
                        {
                            debugln("Calling QueueTagReads in DoWorkGivenRead");
                            QueueTagReads(reads);
                            debugln("Called QueueTagReads in DoWorkGivenRead");

                            debugln("sleepTime=" + sleepTime.ToString());
                            timeEnd = DateTime.Now;
                            sleepTime = sleepTime - Convert.ToInt32(timeEnd.Subtract(timeStart).TotalMilliseconds);
                            if (sleepTime > 0)
                            {
                                Thread.Sleep(sleepTime);
                            }
                        }
                    }
                    else
                    {
                        // Don't eat up all the CPU
                        // TODO: Use a real synchronization construct
                        Thread.Sleep(50);
                        debugln("DoWorkGivenRead spinning on _runNow=false");
                }
            }
                debugln("Loop ended in DoWorkGivenRead: _exitNow=" + _exitNow + " _runNow=" + _runNow);
            }
            catch (ReaderException ex)
            {
                debugln("Caught ReaderException in DoWorkGivenRead:\r\n" + ex.ToString());
                notifyExceptionListeners(ex);

                // Don't call the usual stop method, or we'll deadlock ourselves
                //StopReadingGivenRead();

                // TODO: Stop on fatal exceptions (e.g., serial port failed)
                // especially those that will just lead to more errors.
                // This can hang the system, especially on smaller platforms.
            }
            catch (Exception ex)
            {
                // Need to catch general exceptions, too, or the app will never see them.
                // Unhandled exceptions are bad, especially in Windows CE, which gets very slow
                // while handling them.
                debugln("Caught Exception in DoWorkGivenRead:\r\n" + ex.ToString());
                ReaderException rex = new ReaderException(ex.Message, ex);
                notifyExceptionListeners(rex);
        }
        }

        /// <summary>
        /// Convenience method for delivering reader exceptions to listeners
        /// </summary>
        /// <param name="ex"></param>
        public void notifyExceptionListeners(ReaderException ex)
        {
            ReadExceptionPublisher expub = new ReadExceptionPublisher(this, ex);
            Thread trd = new Thread(expub.OnReadException);
            trd.Name = "OnReadException";
            trd.Start();
        }

        /// <summary>
        /// Submit tag reads for read listener background processing
        /// </summary>
        /// <param name="reads">List of tag reads</param>
        protected void QueueTagReads(ICollection<TagReadData> reads)
        {
            lock (_backgroundNotifierLock)
                _backgroundNotifierCallbackCount++;

            TagReadCallback callback = new TagReadCallback(this, reads);
            
            ThreadPool.QueueUserWorkItem(new WaitCallback(callback.ThreadPoolCallBack), releaseCallBackMethod);
            // Instead of waiting for each tag processing here before going to receive tags from serial buffer
            // we are queuing each tag and go back to receive tags thread. In this process if 100 tags limit is reached
            // we are throwing buffer overflow exception. This happens with slow tag processing in the listeners. With
            // some of the Gen 2 settings, this buffer overflow exception is coming very quickly as we receive tags at 
            // hisgest possible rate and tag processing thread is not getting sufficent time to process before reaching
            // 100 tags limit. To avoid this and to balance between the tag processing thread and tag receiving thread
            // another limit of 20 is introduced, with this if there are 20 tags in queue for processing,we will wait here
            // until tag processing thread gets time to process the tags. 
            if (_backgroundNotifierCallbackCount > 20)
            {
                releaseCallBackMethod.WaitOne(20, false);
                releaseCallBackMethod.Reset();
            }
            // If threadpool has more than 100 tags in queue throw exception as "Buffer overflow" and stop the read.
            if (_backgroundNotifierCallbackCount > 100)
            {
                throw new ReaderCommException("Buffer overflow");
            }
        }

        #endregion

        #region DestroyGivenRead

        /// <summary>
        /// Clean up actions given a working Read method
        /// </summary>
        protected void DestroyGivenRead()
        {
            // TODO: Try to skip StopReading if reader connection is broken
            // (e.g., FTDI serial port invalidated by system suspend)
	    // Safer not to try talking to reader if we know it will fail.
            StopReading();
        }

        #endregion

        #region ReadTagMemWordsGivenReadTagMemBytes

        //// Some tagops can be implemented in terms of other tagops
        /// <summary>
        /// Implement ReadTagMemWords in terms of ReadTagMemBytes
        /// </summary>
        /// <param name="target">Tag to read</param>
        /// <param name="bank">Memory bank identifier</param>
        /// <param name="wordAddress">Word address to start reading at</param>
        /// <param name="wordCount">Number of words to read</param>
        /// <returns>Words read</returns>
        protected internal ushort[] ReadTagMemWordsGivenReadTagMemBytes(TagFilter target, int bank, int wordAddress, int wordCount)
        {
            byte[]   memBytes = ReadTagMemBytes(target, bank, wordAddress * 2, wordCount * 2);
            
            //If word count is zero return all the data
            if (wordCount == 0)
            {
                wordCount = memBytes.Length/2;
            }
            ushort[] memWords = ByteFormat.bytesToWords(memBytes);

            return memWords;
        }

        #endregion

        #endregion

        #region Transport Listener
        /// <summary>
        /// Simple console-output transport listener
        /// </summary>
        public virtual void SimpleTransportListener(Object sender, TransportListenerEventArgs e)
        {
        }

        #endregion Transport Listener

        #region Event Handlers

        #region OnTagRead

        /// <summary>
        /// Internal accessor to TagRead event.
        /// Called by members of the Reader class to fire a TagRead event.
        /// </summary>
        /// <param name="tagReadData">Data from a single tag read</param>
        protected void OnTagRead( TagReadData tagReadData )
        {
            if (null != TagRead)
                TagRead(this, new TagReadDataEventArgs(tagReadData));
        }

        #endregion

        #region OnReadException

        /// <summary>
        /// Publishes ReadException
        /// </summary>
        public class ReadExceptionPublisher
        {
            Reader reader;
            ReaderException exception;

            /// <summary>
            /// Constructor for ReadExceptionPublisher
            /// </summary>
            /// <param name="reader">Reader object</param>
            /// <param name="ex">ReaderException object</param>
            public ReadExceptionPublisher(Reader reader, ReaderException ex)
            {
                this.reader = reader;
                exception = ex;
            }

            /// <summary>
            /// Internal accessor to ReadException event.
            /// Called by members of the Reader class to fire a ReadException event.
            /// </summary>
            public void OnReadException()
            {
                if (null != reader.ReadException)
                    reader.ReadException(this.reader, new ReaderExceptionEventArgs(exception));
            }
        }

        #endregion

        #region OnTransport

        /// <summary>
        /// Fire Transport message event
        /// </summary>
        /// <param name="tx">Message direction: True=host to reader, False=reader to host</param>
        /// <param name="data">Message contents, including framing and checksum bytes</param>
        /// <param name="timeout">Transport timeout setting (milliseconds) when message was sent or received</param>
        protected void OnTransport(bool tx, byte[] data, int timeout)
        {
            if (null != Transport)
                Transport(this, new TransportListenerEventArgs(tx, data, timeout));
        }

        #endregion
        
        #region OnStatusRead
        /// <summary>
        /// Reader Status message event
        /// </summary>
        /// <param name="sReports">array of status reports</param>
        protected  void OnStatusRead(StatusReport[] sReports)
        {
            if (null != StatusListener)
            {
                if (null != StatsListener)
                {
                    throw new ReaderException("Adding both the reader stats and status listener is not supported");
                }
                foreach (StatusReport sr in sReports)
                {
                    StatusListener(this, new StatusReportEventArgs(sr));
                }
            }
        }

        #endregion

        #region OnStatsRead
        /// <summary>
        /// Reader Stats message event
        /// </summary>
        /// <param name="sReport">array of status reports</param>
        protected void OnStatsRead(ReaderStatsReport sReport)
        {
            if (null != StatsListener)
            {
                if (null != StatusListener)
                {
                    throw new ReaderException("Adding both the reader stats and status listener is not supported");
                }
            }
            StatsListener(this, new StatsReportEventArgs(sReport));
        }

        #endregion

        #region OnReadAuthentication

        /// <summary>
        /// Reader Authentication message event.
        /// </summary>
        /// <param name="tagReadData">Data from a single tag read</param>
        protected void OnReadAuthentication(TagReadData tagReadData)
        {
            if (null != ReadAuthentication)
                ReadAuthentication(this, new ReadAuthenticationEventArgs(tagReadData));
        }

        #endregion
        
        #endregion

        #region  Misc Utility Methods

        #region GetFirstConnectedAntenna

        /// <summary>
        /// Pick first available connected antenna
        /// </summary>
        /// <returns>First connected antenna, or 0, if none connected.
        /// (Assumes 0 is never a valid antenna number.)</returns>
        protected int GetFirstConnectedAntenna()
        {
            int[] validAnts = (int[]) ParamGet("/reader/antenna/connectedPortList");

            if (0 < validAnts.Length)
                return validAnts[0];

            return 0;
        }

        #endregion

        #region GetFirstSupportedProtocol

        /// <summary>
        /// Pick first available supported protocol
        /// </summary>
        /// <returns>First supported protocol.  Throws exception if none supported.</returns>
        protected TagProtocol GetFirstSupportedProtocol()
        {
            TagProtocol[] valids = (TagProtocol[]) ParamGet("/reader/version/supportedProtocols");

            if (0 < valids.Length)
                return valids[0];

            throw new ReaderException("No tag protocols available");
        }

        #endregion

        #region ValidateProtocol

        /// <summary>
        /// Is requested protocol a valid protocol?
        /// </summary>
        /// <param name="req">Requested protocol</param>
        /// <returns>req if it is valid, else throws ArgumentException</returns>
        protected TagProtocol ValidateProtocol(TagProtocol req)
        {
            return ValidateParameter<TagProtocol>(req, (TagProtocol[]) ParamGet("/reader/version/supportedProtocols"), "Unsupported protocol");
        }

        #endregion

        #region ValidateParameter

        /// <summary>
        /// Is requested value a valid value?
        /// </summary>
        /// <typeparam name="T">the parameter data type</typeparam>
        /// <param name="req">Requested value</param>
        /// <param name="valids">Array of valid parameters (will be sorted, hope you don't mind.)</param>
        /// <param name="errmsg">Message to use for invalid values; e.g., "Invalid antenna" -> "Invalid antenna: 3"</param>
        /// <returns>Value if valid.  Throws ReaderException if invalid.</returns>
        protected static T ValidateParameter<T>(T req, T[] valids, string errmsg)
        {
            if (IsMember<T>(req, valids))
                return req;

            throw new ReaderException(errmsg + ": " + req);
        }

        #endregion

        #region IsMember

        /// <summary>
        /// Is requested value a valid value?
        /// </summary>
        /// <typeparam name="T"> the member type</typeparam>
        /// <param name="req">Requested value</param>
        /// <param name="valids">Array of valid parameters (will be sorted, hope you don't mind.)</param>
        /// <returns>True if value is member of list.  False otherwise.</returns>
        protected static bool IsMember<T>(T req, T[] valids)
        {
            Array.Sort(valids);

            if (0 <= Array.BinarySearch(valids, req))
                return true;

            return false;
        }

        #endregion

        #endregion

        #region isAntDetectEnabled
        /// <summary>
        /// Method to check antenna detection is supported or not
        /// </summary>
        /// <param name="antennaList">Contains set of antennas provided by user</param>
        /// <returns>bool representing value</returns>
        public bool isAntDetectEnabled(ICollection<int> antennaList)
        {
            string model = (string)ParamGet("/reader/version/model").ToString();
            Boolean checkPort = (Boolean)ParamGet("/reader/antenna/checkPort");
            String swVersion = (String)ParamGet("/reader/version/software");

            if ((model.Equals("M6e Micro") || model.Equals("M6e Nano") ||
                (model.Equals("Sargas") && (swVersion.StartsWith("5.1")))) //||
                //((model.Equals("Izar") || model.Equals("Astra200") && (Convert.ToInt16(swVersion) >= 5))
                && (false == checkPort) && antennaList == null)
            {
                return true;
            }
            return false;
        }

        #endregion
    }

    /// <summary>
    /// Memory Type - Type of memory operation
    /// </summary>
    #region MemoryType

    public enum MemoryType
    {
        /// <summary>
        /// Block memory- Both read and write are supported
        /// </summary>
        BLOCK_MEMORY = 0x21,
        /// <summary>
        /// Reads system information of tag
        /// </summary>
        BLOCK_SYSTEM_INFORMATION_MEMORY = 0x22,
        /// <summary>
        /// Reads block protection status of tag
        /// </summary>
        BLOCK_PROTECTION_STATUS_MEMORY = 0x23, 
        /// <summary>
        ///  Reads secure id of tag
        /// </summary>
        SECURE_ID = 0x24,
    }

    #endregion

    #region WriteMemory
    /// <summary>
    /// WriteMemory
    /// </summary>
    public class WriteMemory : TagOp
    {
        #region Fields

        /// <summary>
        /// the type of memory operation
        /// </summary>
        public MemoryType memType;

        /// <summary>
        /// the address of the memory location to start write data into
        /// </summary>
        public UInt32 address;

        /// <summary>
        /// the data to be written
        /// </summary>
        public byte[] data;

        #endregion

        #region Construction
        /// <summary>
        /// Constructor to initialize the parameters of WriteMemory
        /// </summary>
        /// <param name="memType">the type of write operation</param>
        /// <param name="address">address to start write data into</param>
        /// <param name="data">the data to write</param>
        public WriteMemory(MemoryType memType, UInt32 address, byte[] data)
        {
            this.memType = memType;
            this.address = address;
            this.data = data;
        }

        #endregion
    }

    #endregion

    #region ReadMemory
    /// <summary>
    /// ReadMemory
    /// </summary>
    public class ReadMemory : TagOp
    {
        #region Fields

        /// <summary>
        /// the type of memory operation
        /// </summary>
        public MemoryType memType;

        /// <summary>
        /// the address of the memory location to start read data from
        /// </summary>
        public UInt32 address;

        /// <summary>
        /// number of memory units to read
        /// </summary>
        public byte length;

        #endregion

        #region Construction
        /// <summary>
        /// Constructor to initialize the parameters of ReadMemory
        /// </summary>
        /// <param name="memType">the type of memory operation</param>
        /// <param name="address">the address of the memory location to start read data from</param>
        /// <param name="length">number of memory units to read</param>
        public ReadMemory(MemoryType memType, UInt32 address, byte length)
        {
            this.memType = memType;
            this.address = address;
            this.length = length;
        }

        #endregion
    }
    #endregion

    #region Select_TagType
    /// <summary>
    /// select filter with TagType
    /// </summary>
    public class Select_TagType : TagFilter
    {
        #region Fields

        ///<summary>
        ///Holds the tag type
        ///</summary>
        public UInt64 tagType;

        #endregion

        #region Construction

        /// <summary>
        /// Create Select filter based on tagtype
        /// </summary>
        /// <param name="tagType"> TagType to be considered for filtering</param>
        public Select_TagType(UInt64 tagType)
        {
            this.tagType = tagType;
        }
        #endregion

        #region Matches

        /// <summary>
        /// Test if a tag Matches this filter. Only applies to selects based
        /// on the UID.
        /// </summary>
        /// <param name="t">tag data to screen</param>
        /// <returns>Return true to allow tag through the filter.
        /// Return false to reject tag.</returns>
        public bool Matches(ThingMagic.TagData t)
        {
            throw new NotSupportedException();
        }

        #endregion
    }
    #endregion

    #region Select_UID
    /// <summary>
    /// select filter on UID
    /// </summary>
    public class Select_UID : TagFilter
    {
        #region Fields

        ///<summary>
        ///UID bit length
        ///</summary>
        public byte bitLength;

        ///<summary>
        ///Filter mask
        ///</summary>
        public byte[] uidMask;

        #endregion

        #region Construction

        /// <summary>
        /// Create Select on UID filter
        /// </summary>
        /// <param name="bitLength">The length (in bits) of the mask</param>
        /// <param name="uidMask">The mask value to compare with the specified region of tag memory, MSB first</param>
        public Select_UID(byte bitLength, ICollection<byte> uidMask)
        {
            this.bitLength = bitLength;
            this.uidMask = CollUtil.ToArray(uidMask);
        }
        #endregion

        #region Matches

        /// <summary>
        /// Test if a tag Matches this filter. Only applies to selects based
        /// on the UID.
        /// </summary>
        /// <param name="t">tag data to screen</param>
        /// <returns>Return true to allow tag through the filter.
        /// Return false to reject tag.</returns>
        public bool Matches(ThingMagic.TagData t)
        {
            throw new NotSupportedException();
        }

        #endregion
    }
    #endregion

    #region ConfigFlags
    /// <summary>
    /// enum to define Configuration flags
    /// </summary>
    [Flags]
    public enum ConfigFlags
    {
        /// <summary>
        /// enables TX CRC
        /// </summary>
        ENABLE_TX_CRC = (1 << 0),
        /// <summary>
        /// enables RX CRC
        /// </summary>
        ENABLE_RX_CRC = (1 << 1),
        /// <summary>
        /// enables Inventory
        /// </summary>
        ENABLE_INVENTORY = (1 << 2),
    }

    #endregion

    #region PassThrough
    /// <summary>
    /// PassThrough tag operation
    /// </summary>
    public class PassThrough : TagOp
    {
        #region Fields

        /// <summary>
        /// Timeout in msec 
        /// </summary>
        public UInt32 timeout;

        /// <summary>
        /// Configuration flags - RFU 
        /// </summary>
        public UInt32 configFlags;

        /// <summary>
        /// Command buffer 
        /// </summary>
        public List<byte> buffer;

        #endregion

        #region Construction
        /// <summary>
        /// Constructor to initialize the parameters of PassThrough
        /// </summary>
        /// <param name="timeout">Timeout in msec </param>
        /// <param name="configFlags">Configuration flags - RFU</param>
        /// <param name="buffer">Command buffer </param>
        public PassThrough(UInt32 timeout, UInt32 configFlags, List<byte> buffer)
        {
            this.timeout = timeout;
            this.configFlags = configFlags;
            this.buffer = buffer;
        }
        #endregion
    }
    #endregion

    #region SupportedTagFeatures
    /// <summary>
    /// enum to define SupportedTagFeatures
    /// </summary>
    [Flags]
    public enum SupportedTagFeatures
    {
        /// <summary>
        /// NONE
        /// </summary>
        NONE = 0x00000000,
        /// <summary>
        /// HF HID ICLASS SE SECURE READ
        /// </summary>
        HF_HID_ICLASS_SE_SECURE_RD = 0x00000001,
        /// <summary>
        /// LF HID PROX SECURE READ
        /// </summary>
        LF_HID_PROX_SECURE_RD = 0x00000010,
    }
    #endregion
}
