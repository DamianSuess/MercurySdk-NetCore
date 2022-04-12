/*
 * Copyright (c) 2011 ThingMagic, Inc.
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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Org.LLRP.LTK.LLRPV1;
using LTKD = Org.LLRP.LTK.LLRPV1.DataType;
using Org.LLRP.LTK.LLRPV1.thingmagic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
#if !WindowsCE
using System.Timers;
#endif
namespace ThingMagic
{
    /// <summary>
    /// The RqlReader class is an implementation of a Reader object that 
    /// communicates with a ThingMagic fixed RFID reader via the Low level reader protocol.
    /// 
    /// Instances of the Llrp class are created with the Reader.create()method with a 
    /// "llrp" URI or a generic "tmr" URI that references a network device.
    /// </summary>
    public class LlrpReader : Reader
    {
        #region Fields
        // Flag to check whether firmware upgrade/downgrade is in progress or no
        bool isFirmwareLoadInProgress = false;
        //Create an instance of LLRP reader client.
        LLRPClient llrp;
        //To accomodate all the all the messages.
        MSG_ERROR_MESSAGE errorMessage;
        private string hostName;
        private int portNumber;
        List<TagReadData> tagReads;
        private bool continuousReading = false;
        private int antennaMax;
        private int[] antennaPorts;
        private int rfPowerMax;
        private int rfPowerMin;
        private string softwareVersion;
        private bool multiselectSupport = false;
        private bool perAntOnTimeSupport = false;
        private bool addInvSpecIDSupport = false;
        private bool customMetaDataSupport = false;
        private bool statListSupport = false;
        private bool isStateAwareTargetMapped = false;
        private string model;
        private int[] gpiList;
        private int[] gpoList;
        private Region regionId;
        private PARAM_TransmitPowerLevelTableEntry[] PowerTableEntry = null;
        PARAM_FrequencyHopTable[] frequencyHopTable;
        List<uint> freq;
        private const int TM_MANUFACTURER_ID = 26554;
        private Hashtable RFModeCache = null, PowerValueTable = null, PowerIndexTable = null;
        //roSpecList needs to be global, as the same needs to be accessed by different methods.
        List<PARAM_ROSpec> roSpecList;
        //Queue to put the tags recevied from LLRP reader
        private Queue tagReadQueue = new Queue();
        //Queue to put the RF report recevied from LLRP reader
        private Queue RFSurvyQueue = new Queue();
        private readonly object reportlock = new object();
        //Thread to process the RO access reports
        private Thread asyncReadThread;
        private Thread rFReportThread;
        uint roSpecId = 0;
        /// <summary>
        /// Status will be changed on end of AI Spec
        /// </summary>
        protected bool endOfAISpec = false;
        private ushort OpSpecID = 0;
        private uint AccessSpecID = 0;
        bool reportReceived = false;
        // Stop trigger feature enabled or disabled
        private bool isStopNTags = false;
        // Cache number of tags to read
        private uint numberOfTagsToRead = 0;
        /// <summary>
        /// Bitmask of "Is this ROSpec ended?" bits.
        /// Works as long as we never use more than ROSpecs 1-64.
        /// e.g., if (RoSpecFlags[ROSpecID-1]), then ROSpecID is active
        /// To wait for all ROSpecs to end, check for 0==ROSpecFlags
        /// </summary>
        UInt64 ROSpecFlags = 0;
        /// <summary>
        /// Set when 0==ROSpecFlags; i.e., no ROSpecs are active (i.e., all ROSpecs have ended)
        /// </summary>
        ManualResetEvent ROSpecFlagsZeroed = new ManualResetEvent(true);
        /// <summary>
        /// Set when 0!=ROSpecFlags; i.e., ROSpecs are active (i.e., ROSpecs have started)
        /// </summary>
        ManualResetEvent ROSpecFlagsSet = new ManualResetEvent(false);
        private Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private object tagOpResponse;
        private ReaderException rx;
        int keepAliveTrigger = 5000;
        //Indicates start time of any operation.Any time an LLRP message is sent or received, msgStartTime is updated.
        DateTime msgStartTime = DateTime.Now;
        // capture the type of message sent here
        LTKD.Message msgSent = null;
        // Flag to indicate RESPONSE is received
        bool isMsgRespReceived = false;
        // Maximum milliseconds required for reader to stop an ongoing search
        int STOP_TIMEOUT = 5000;
        DateTime keepAliveTime = DateTime.Now;
#if !WindowsCE
        System.Timers.Timer monitorKeepAlives;
#endif
        bool isRoAccessReportsComing = false;
        object isRoAccessReportsComingLock = new object();
        Hashtable roSpecProtocolTable = null;
        // Error code for Nullreference exception
        private const uint ErrCodeNullObjectReference = 0x80004003;
        private uint invSpecId = 0;
        private int maxSubPlanCnt = 5; /*For TMreader build <5.3.2.93*/

        ENUM_ThingMagicCustomMetadataFlag metadataflag = ENUM_ThingMagicCustomMetadataFlag.MetadataAll;

        #endregion Fields

        #region ThingMagicDeDuplication

        private enum ThingMagicDeDuplication
        {
            UniqueByAntenna = 0,
            UniqueByData = 1,
            RecordHighestRssi = 3,
        }
        #endregion ThingMagicDeDuplication

        #region ISO18K6BProtocolConfigurationParams

        private enum ISO18K6BProtocolConfigurationParams
        {
            LinkFrequency = 0,
            Delimiter = 1,
            ModulationDepth = 2,
        }
        #endregion ISO18K6BProtocolConfigurationParams

        #region ThingMagicPower
        /// <summary>
        /// ThingMagicPower
        /// </summary>
        private enum ThingMagicPower
        {
            /// <summary>
            /// Port Read Power List
            /// </summary>
            PortReadPowerList = 1,
            /// <summary>
            /// Global Read Power
            /// </summary>
            ReadPower = 2,
            /// <summary>
            /// Port Write Power List
            /// </summary>
            PortWritePowerList = 3,
            /// <summary>
            /// Global Write Power
            /// </summary>
            WritePower = 4,
        }
        #endregion ThingMagicPower

        #region PowerIndex
        /// <summary>
        /// Power index
        /// </summary>
        private enum PowerIndex
        {
            /// <summary>
            /// Power Value
            /// </summary>
            PowerValue = 1,
            /// <summary>
            /// Power Index
            /// </summary>
            IndexValue = 2,
        }
        #endregion PowerIndex

        /// <summary>
        /// Connect to LLRP reader on default port (5084)
        /// </summary>
        /// <param name="host">hostName of llrp reader</param>
        /// <param name="port">Port Number of llrp reader</param>        
        public LlrpReader(string host, int port)
        {
            if (port < 0)
            {
                portNumber = 5084;
                llrp = new LLRPClient();
            }
            else
            {
                portNumber = port;
                llrp = new LLRPClient(portNumber);
            }
            hostName = host;
        }

        /// <summary>
        /// Connect to LLRP reader on default port (5084)
        /// </summary>
        /// <param name="host">hostName of llrp reader</param>               
        public LlrpReader(string host)
        {
            llrp = new LLRPClient();
            hostName = host;
        }

        /// <summary>
        /// Connect reader object to device.
        /// If object already connected, then do nothing.
        /// </summary>
        public override void Connect()
        {
            int ConnectionTimeOut = 0;
            ENUM_ConnectionAttemptStatusType status;
            try
            {
                if (!llrp.IsConnected)
                {
                    ConnectionTimeOut = (int)ParamGet("/reader/transportTimeout");
                    if (ConnectionTimeOut < 10100)
                    {
                        ConnectionTimeOut = 10100;
                    }
                    llrp.Open(hostName, ConnectionTimeOut, out status);

                    //LTK is taking care of waiting for READER_EVENT_NOTIFICATION verification

                    // Check for a connection error
                    if (status != ENUM_ConnectionAttemptStatusType.Success)
                    {
                        // Could not connect to the reader.
                        throw new ReaderCommException(status.ToString());
                    }
                }
                llrp.OnReaderEventNotification += new delegateReaderEventNotification(OnReaderEventNotificationReceived);
                thingmagic_Installer.Install();
                SetHoldReportsAndEvents(true);

                //Stop active reports if any
                StopActiveROSpecs();
                //Get the hardware version
                ProbeHardware();
                //Hold the reports and events
                //EnableEventsAndReports();
                //Set the keep alives
                SetKeepAlive();
#if !WindowsCE
                monitorKeepAlives = new System.Timers.Timer(keepAliveTrigger * 2);
                monitorKeepAlives.Elapsed += new ElapsedEventHandler(OnTimedEvent);
                monitorKeepAlives.Enabled = true;
                monitorKeepAlives.Start();
#endif
                asyncReadThread = new Thread(ProcessRoAccessReport);
                asyncReadThread.Name = "ProcessRoAccessReport";
                asyncReadThread.IsBackground = true;
                asyncReadThread.Start();

                rFReportThread = new Thread(ProcessRRFReport);
                rFReportThread.Name = "ProcessRRFReport";
                rFReportThread.IsBackground = true;
                rFReportThread.Start();

                InitParams();
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }

        #region ReceiveAutonomousReading
        /// <summary>
        /// Receives data from module continuously
        /// </summary>
        public override void ReceiveAutonomousReading()
        {
            throw new FeatureNotSupportedException("Unsupported operation");
        }
        #endregion

        #region Reboot
        /// <summary>
        /// Reboots the reader device
        /// </summary>
        public override void Reboot()
        {
            try
            {
                if (null != llrp)
                {
                    MSG_THINGMAGIC_CONTROL_REQUEST_POWER_CYCLE_READER reqPowerCycle = new MSG_THINGMAGIC_CONTROL_REQUEST_POWER_CYCLE_READER();
                    reqPowerCycle.BootToSafeMode = false;
                    reqPowerCycle.MagicNumber = (uint)0x20000920;

                    MSG_THINGMAGIC_CONTROL_RESPONSE_POWER_CYCLE_READER response;
                    response = (MSG_THINGMAGIC_CONTROL_RESPONSE_POWER_CYCLE_READER)SendLlrpMessage(reqPowerCycle);
                    Thread.Sleep(90000);
                    if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                    {
                        Console.WriteLine("Reader rebooted successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }
        #endregion

        /// <summary>
        /// Checks the reader is RQL reader or Llrp Reader
        /// </summary>
        /// <returns>true/false</returns>
        protected internal bool IsLlrpReader()
        {
            int ConnectionTimeOut = 0;
            ENUM_ConnectionAttemptStatusType status;
            try
            {
                ConnectionTimeOut = (int)ParamGet("/reader/transportTimeout");
                if (ConnectionTimeOut < 10100)
                {
                    ConnectionTimeOut = 10100;
                }
                connectionErrors = "Tried to connect to LLRP Reader with host name:" + hostName.ToString() + "\n";
                llrp.Open(hostName, ConnectionTimeOut, out status);
                if (status != ENUM_ConnectionAttemptStatusType.Success)
                {
                    connectionErrors += status.ToString() + "\n";
                }

                MSG_GET_READER_CAPABILITIES msgCapab = new MSG_GET_READER_CAPABILITIES();
                msgCapab.RequestedData = ENUM_GetReaderCapabilitiesRequestedData.General_Device_Capabilities;
                MSG_GET_READER_CAPABILITIES_RESPONSE msgCapabRes = (MSG_GET_READER_CAPABILITIES_RESPONSE)SendLlrpMessage(msgCapab);

                if (null == msgCapabRes)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void StopActiveROSpecs()
        {
            MSG_GET_ROSPECS_RESPONSE response = null;

            MSG_GET_ROSPECS roSpecs = new MSG_GET_ROSPECS();
            try
            {
                response = (MSG_GET_ROSPECS_RESPONSE)SendLlrpMessage(roSpecs);
                if (null != response)
                {
                    PARAM_ROSpec[] roSpecList = response.ROSpec;

                    if (null != roSpecList)
                    {
                        foreach (PARAM_ROSpec rSpec in roSpecList)
                        {
                            if (rSpec.CurrentState == ENUM_ROSpecState.Active)
                            {
                                StopRoSpec(rSpec.ROSpecID);
                            }
                        }
                    }
                }
            }
            catch (ReaderException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }
        /// <summary>
        /// Shuts down the connection with the reader device.
        /// </summary>
        public override void Destroy()
        {
#if !WindowsCE
            if (null != monitorKeepAlives)
            {
                monitorKeepAlives.Stop();
                monitorKeepAlives.Elapsed -= new ElapsedEventHandler(OnTimedEvent);
            }
#endif
            if (null != llrp)
            {
                //Signal the aysncReadThread to smothly come out
                stopAsyncReadThread = true;
                llrp.Close();
                llrp.OnReaderEventNotification -= new delegateReaderEventNotification(OnReaderEventNotificationReceived);
                llrp.OnKeepAlive -= new delegateKeepAlive(OnKeepAliveReceived);
                socket = null;
                tagReadQueue = null;
                RFSurvyQueue = null;

                if (!isFirmwareLoadInProgress)
                {
                    llrp = null;
                }
            }
            GC.Collect();
        }

        #region InitParams

        private void InitParams()
        {
            ParamAdd(new Setting("/reader/region/id", typeof(Region), null, false,
                delegate(Object val) { val = regionId; return val; }, null));
            ParamAdd(new Setting("/reader/region/supportedRegions", typeof(Region[]), null, false,
                delegate(Object val) { return new Region[] { regionId }; }, null));
            ParamAdd(new Setting("/reader/region/hopTable", typeof(int[]), null, true,
                delegate(Object val) { return getRegulatoryCapabilities(); },
                delegate(Object val)
                {
                    int[] hoptableList = (int[])val;
                    if (hoptableList.Length <= 0)
                    {
                        throw new ArgumentException("Hoptable cannot be empty.");
                    }
                    return SetRegionHopTable(val);
                }));
            ParamAdd(new Setting("/reader/antenna/portList", typeof(int[]), null, false,
                GetAntennaPortList, null));
            ParamAdd(new Setting("/reader/antenna/connectedPortList", typeof(int[]), null, false, GetConnectedPortList, null));
            ParamAdd(new Setting("/reader/antenna/portSwitchGpos", typeof(int[]), null, true,
                delegate(Object val)
                {
                    throw new ArgumentException("Unsupported Operation");
                },
                SetThingMagicPortSwitchGPOs
                ));
            ParamAdd(new Setting("/reader/radio/powerMax", typeof(int), rfPowerMax, false));
            ParamAdd(new Setting("/reader/radio/powerMin", typeof(int), rfPowerMin, false));
            ParamAdd(new Setting("/reader/radio/portReadPowerList", typeof(int[][]), null, true,
                delegate(Object val) { return GetPortPowerList(val, ThingMagicPower.PortReadPowerList); },
                delegate(Object val) { return SetPortPowerList(val, ThingMagicPower.PortReadPowerList); }, false));
            ParamAdd(new Setting("/reader/version/serial", typeof(string), null, false, GetVersionSerial, null));
            ParamAdd(new Setting("/reader/version/model", typeof(string), model, false));
            ParamAdd(new Setting("/reader/version/software", typeof(string), softwareVersion, false));
            ParamAdd(new Setting("/reader/read/plan", typeof(ReadPlan), new SimpleReadPlan(), true, null,
                delegate(Object val)
                {
                    if ((val is SimpleReadPlan) || (val is MultiReadPlan)) { return val; }
                    else { throw new ArgumentException("Unsupported /reader/read/plan type: " + val.GetType().ToString() + "."); }
                }));
            ParamAdd(new Setting("/reader/gpio/inputList", typeof(int[]), gpiList, false));
            ParamAdd(new Setting("/reader/gpio/outputList", typeof(int[]), gpoList, false));
            ParamAdd(new Setting("/reader/description", typeof(string), null, true,
                GetReaderDescription, SetReaderDescription, false));
            ParamAdd(new Setting("/reader/hostname", typeof(string), null, true,
                GetReaderHostName, SetReaderHostName, false));
            ParamAdd(new Setting("/reader/version/hardware", typeof(string), null, false, GetVersionHardware, null));
            ParamAdd(new Setting("/reader/version/productID", typeof(int), null, false, GetProductID, null));
            ParamAdd(new Setting("/reader/version/productGroup", typeof(string), null, false, GetProductGroup, null));
            ParamAdd(new Setting("/reader/version/productGroupID", typeof(int), null, false, GetProductGroupID, null));
            ParamAdd(new Setting("/reader/radio/temperature", typeof(int), null, false, GetReaderModuleTemperature, null));
            ParamAdd(new Setting("/reader/currentTime", typeof(DateTime), null, false, GetCurrentTime, null));
            ParamAdd(new Setting("/reader/tagReadData/uniqueByAntenna", typeof(bool), false, true,
                delegate(Object val) { return GetThingMagicDeDuplicationFields(val, ThingMagicDeDuplication.UniqueByAntenna); },
                delegate(Object val) { return SetThingMagicDeDuplicationFields(val, ThingMagicDeDuplication.UniqueByAntenna); }, false));
            ParamAdd(new Setting("/reader/tagReadData/uniqueByData", typeof(bool), false, true,
                delegate(Object val) { return GetThingMagicDeDuplicationFields(val, ThingMagicDeDuplication.UniqueByData); },
                delegate(Object val) { return SetThingMagicDeDuplicationFields(val, ThingMagicDeDuplication.UniqueByData); }, false));
            ParamAdd(new Setting("/reader/tagReadData/recordHighestRssi", typeof(bool), false, true,
                delegate(Object val) { return GetThingMagicDeDuplicationFields(val, ThingMagicDeDuplication.RecordHighestRssi); },
                delegate(Object val) { return SetThingMagicDeDuplicationFields(val, ThingMagicDeDuplication.RecordHighestRssi); }, false));
            ParamAdd(new Setting("/reader/antenna/checkPort", typeof(bool), null, true,
                GetAntennaDetection, SetAntennaDetection));
            //Add asyncOnTime if supported firmware is used
            if (perAntOnTimeSupport == true)
            {
                ParamAdd(new Setting("/reader/read/asyncOnTime", typeof(int), null, true,
                    GetAsyncOnTime, SetAsyncOnTime));
            }
            ParamAdd(new Setting("/reader/read/asyncOffTime", typeof(int), null, true,
                GetAsyncOffTime, SetAsyncOffTime));
            ParamAdd(new Setting("/reader/version/supportedProtocols", typeof(TagProtocol[]), null, false, GetSupportedProtocols, null));
            ParamAdd(new Setting("/reader/gen2/session", typeof(Gen2.Session), null, true, GetSession, SetSession));
            ParamAdd(new Setting("/reader/gen2/protocolExtension", typeof(Gen2.ProtocolExtension), null, false, GetProtocolExtension, null));
            ParamAdd(new Setting("/reader/gen2/tagEncoding", typeof(Gen2.TagEncoding), null, true,
                delegate(Object val)
                {
                    return Enum.Parse(typeof(Gen2.TagEncoding), GetGen2Param("TagEncoding").ToString(), true);
                },
                SetTagEncoding, false));
            ParamAdd(new Setting("/reader/gen2/q", typeof(Gen2.Q), null, true, GetGen2Q, SetGen2Q));
            ParamAdd(new Setting("/reader/gen2/InitQ", typeof(Gen2.InitQ), null, true, GetGen2InitQ, SetGen2InitQ));
            ParamAdd(new Setting("/reader/gen2/SendSelect", typeof(bool), null, true, GetGen2SendSelect, SetGen2SendSelect));
            ParamAdd(new Setting("/reader/gen2/BLF", typeof(Gen2.LinkFrequency), null, true,
                delegate(Object val)
                {
                    return GetLinkFrequency((GetGen2Param("LinkFrequency").ToString()));
                }, SetGen2BLF, false));
            ParamAdd(new Setting("/reader/gen2/tari", typeof(Gen2.Tari), null, true,
                delegate(Object val)
                {
                    PARAM_C1G2RFControl rfControl = GetRFcontrol();
                    return GetTariEnum((int)rfControl.Tari);
                }, SetTari, false));
            ParamAdd(new Setting("/reader/antenna/returnLoss", typeof(int[][]), null, false, GetAntennaReturnLoss, null));
            ParamAdd(new Setting("/reader/metadata", typeof(SerialReader.TagMetadataFlag), null, true, GetReaderMetadata, SetReaderMetadata));

            ParamAdd(new Setting("/reader/stats/enable", typeof(Reader.Stat.StatsFlag), null, true, GetTMStatsEnable, SetTMStatsEnable));
            ParamAdd(new Setting("/reader/stats", typeof(Reader.Stat.Values), null, false, GetTMStatsValue, null));

            ParamAdd(new Setting("/reader/regulatory/mode", typeof(RegulatoryMode), null, true, GetRegulatoryMode,
                SetRegulatoryMode));
            ParamAdd(new Setting("/reader/regulatory/modulation", typeof(RegulatoryModulation), null, true, GetRegulatoryModulation,
                SetRegulatoryModulation));
            ParamAdd(new Setting("/reader/regulatory/onTime", typeof(int), null, true, GetRegulatoryOnTime,
                SetRegulatoryonTime));
            ParamAdd(new Setting("/reader/regulatory/offTime", typeof(int), null, true, GetRegulatoryOffTime,
                SetRegulatoryOffTime));
            ParamAdd(new Setting("/reader/regulatory/enable", typeof(bool), null, true,
                delegate(Object val)
                {
                    throw new ArgumentException("Unsupported Operation");
                }, SetRegulatoryEnable));
            ParamAdd(new Setting("/reader/radio/readPower", typeof(int), null, true,
                delegate(Object val) { return GetPortPowerList(val, ThingMagicPower.ReadPower); },
                delegate(Object val) { return SetPortPowerList(val, ThingMagicPower.ReadPower); }));
            ParamAdd(new Setting("/reader/gen2/target", typeof(Gen2.Target), null, true,
                GetCustomGen2Target, SetCustomGen2Target, false));
            ParamAdd(new Setting("/reader/gen2/T4", typeof(UInt32), null, true, GetCustomGen2T4, SetCustomGen2T4));
            ParamAdd(new Setting("/reader/radio/portWritePowerList", typeof(int[][]), null, true,
                delegate(Object val) { return GetPortPowerList(val, ThingMagicPower.PortWritePowerList); },
                delegate(Object val) { return SetPortPowerList(val, ThingMagicPower.PortWritePowerList); }, false));
            ParamAdd(new Setting("/reader/radio/writePower", typeof(int), null, true,
                delegate(Object val) { return GetPortPowerList(val, ThingMagicPower.WritePower); },
                delegate(Object val) { return SetPortPowerList(val, ThingMagicPower.WritePower); }));
            ParamAdd(new Setting("/reader/licenseKey", typeof(ICollection<byte>), null, true,
                delegate(Object val)
                {
                    throw new FeatureNotSupportedException("Unsupported operation");
                },
                delegate(Object val)
                {
                    return SetThingMagicLicenseKey(val);
                }));
            ParamAdd(new Setting("/reader/manageLicenseKey", typeof(ThingMagic.Reader.LicenseOperation), null, true,
                delegate(Object val)
                {
                    throw new FeatureNotSupportedException("Unsupported operation");
                },
                delegate(Object val)
                {
                    ThingMagic.Reader.LicenseOperation operation = (ThingMagic.Reader.LicenseOperation)val;
                    if (operation.option == ThingMagic.Reader.LicenseOption.SET_LICENSE_KEY)
                    {
                        SetThingMagicLicenseKey(operation.key);
                    }
                    else
                    {
                        throw new FeatureNotSupportedException("Unimplemented feature");
                    }
                    return null;
                }));
            ParamAdd(new Setting("/reader/tagop/antenna", typeof(int), GetFirstConnectedAntenna(), true,
                null,
                delegate(Object val) { return ValidateAntenna((int)val); }));
            ParamAdd(new Setting("/reader/tagop/protocol", typeof(TagProtocol), TagProtocol.GEN2, true,
                null,
                delegate(Object val) { return ValidateProtocol((TagProtocol)val); }));
            ParamAdd(new Setting("/reader/iso180006b/delimiter", typeof(Iso180006b.Delimiter), null, true,
                delegate(Object val)
                {
                    int protocolConfigParam = 0;
                    protocolConfigParam = GetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams.Delimiter);
                    return Enum.Parse(typeof(Iso180006b.Delimiter), protocolConfigParam.ToString(), false);
                },
                delegate(Object val)
                {
                    return SetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams.Delimiter, val);
                }, false));
            ParamAdd(new Setting("/reader/iso180006b/modulationDepth", typeof(Iso180006b.ModulationDepth), null, true,
                delegate(Object val)
                {
                    int protocolConfigParam = 0;
                    protocolConfigParam = GetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams.ModulationDepth);
                    return Enum.Parse(typeof(Iso180006b.ModulationDepth), protocolConfigParam.ToString(), false);
                }, delegate(Object val)
                {
                    return SetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams.ModulationDepth, val);
                }, false));
            ParamAdd(new Setting("/reader/iso180006b/BLF", typeof(Iso180006b.LinkFrequency), null, true,
                delegate(Object val)
                {
                    int protocolConfigParam = 0;
                    protocolConfigParam = GetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams.LinkFrequency);
                    return Enum.Parse(typeof(Iso180006b.LinkFrequency), protocolConfigParam.ToString(), false);
                }, delegate(Object val)
                {
                    return SetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams.LinkFrequency, val);
                }, false));
        }
        #endregion InitParams

        #region Get Antenna Portlist
        /// <summary>
        /// Gets the Antenna Port List
        /// </summary>
        private void InitializeAntennaList()
        {
            //get antenna portlist
            int antennaport = 0;

            MSG_GET_READER_CONFIG msgConfig = new MSG_GET_READER_CONFIG();
            MSG_GET_READER_CONFIG_RESPONSE msgConfigRes;
            try
            {
                msgConfig.AntennaID = 0;
                msgConfig.RequestedData = ENUM_GetReaderConfigRequestedData.AntennaProperties;
                msgConfigRes = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            //Build antenna portlist
            List<int> antennaPortlist = new List<int>();
            if (null == msgConfigRes)
            {
                throw new Exception("Not able to get reader configuration");
            }
            PARAM_AntennaProperties[] ant = msgConfigRes.AntennaProperties;
            antennaMax = ant.Length;
            for (int port = 0; port < antennaMax; port++)
            {
                try
                {
                    antennaport = Convert.ToInt32(msgConfigRes.AntennaProperties[port].AntennaID);
                    antennaPortlist.Add(antennaport);
                }
                catch (Exception ex)
                {
                    throw new ReaderException(ex.Message);
                }
            }

            antennaPorts = antennaPortlist.ToArray();
        }

        #endregion Get Antenna Portlist

        #region ProbeHardware

        private void ProbeHardware()
        {
            //get antenna portlist

            InitializeAntennaList();

            //Get Power max and power min
            MSG_GET_READER_CAPABILITIES msgCapab = new MSG_GET_READER_CAPABILITIES();
            MSG_GET_READER_CAPABILITIES_RESPONSE msgCapabRes;
            try
            {
                msgCapab.RequestedData = ENUM_GetReaderCapabilitiesRequestedData.All;
                msgCapabRes = (MSG_GET_READER_CAPABILITIES_RESPONSE)SendLlrpMessage(msgCapab);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            //Build Power max and power min
            List<int> pwrlist = new List<int>();
            PARAM_TransmitPowerLevelTableEntry[] powertable = msgCapabRes.RegulatoryCapabilities.UHFBandCapabilities.TransmitPowerLevelTableEntry;
            for (int PwrLevelTableIndex = 0; PwrLevelTableIndex < powertable.Length; PwrLevelTableIndex++)
            {
                try
                {
                    int value = Convert.ToInt32(msgCapabRes.RegulatoryCapabilities.UHFBandCapabilities.TransmitPowerLevelTableEntry[PwrLevelTableIndex].TransmitPowerValue);
                    pwrlist.Add(value);
                }
                catch (Exception ex)
                {
                    throw new ReaderException(ex.Message);
                }
            }
            pwrlist.Sort();
            rfPowerMin = pwrlist[0];
            rfPowerMax = pwrlist[(powertable.Length) - 1];
            //cache frequency hop table
            frequencyHopTable = msgCapabRes.RegulatoryCapabilities.UHFBandCapabilities.FrequencyInformation.FrequencyHopTable;
            //Extract the frequency hoptable
            freq = frequencyHopTable[0].Frequency.data;
            //cache the PowerTableEntry 
            PowerTableEntry = msgCapabRes.RegulatoryCapabilities.UHFBandCapabilities.TransmitPowerLevelTableEntry;
            //Build table from PowerTableEntry to get transmitpwrvalue and index
            PowerValueTable = new Hashtable();
            PowerIndexTable = new Hashtable();
            foreach (PARAM_TransmitPowerLevelTableEntry pwrEntry in PowerTableEntry)
            {
                PowerValueTable.Add(pwrEntry.Index, pwrEntry.TransmitPowerValue);
                PowerIndexTable.Add(pwrEntry.TransmitPowerValue, pwrEntry.Index);
            }
            //cache the UHF band capabilities to set blf and tari
            PARAM_UHFBandCapabilities capabilities = msgCapabRes.RegulatoryCapabilities.UHFBandCapabilities;
            CacheRFModeTable(capabilities);
            //model
            if (msgCapabRes.GeneralDeviceCapabilities.DeviceManufacturerName == TM_MANUFACTURER_ID) //ThingMagic vendor id
            {
                //building model as M6
                switch (msgCapabRes.GeneralDeviceCapabilities.ModelName)
                {
                    case 0x06:
                        model = "Mercury6";
                        break;
                    case 0x30:
                        model = "Astra-EX";
                        break;
                    case 0x3430:
                        model = "Sargas";
                        break;
                    case 0x3530:
                        model = "Izar";
                        break;
                    case 0x3630:
                        model = "Astra200";
                        break;
                    default:
                        model = "Unknown";
                        break;
                }
            }
            //GPIO
            if (model.Equals("Mercury6") || model.Equals("Astra-EX"))
            {
                //GPIO InputList
                gpiList = new int[] { 3, 4, 6, 7 };
                //GPIO OutputList
                gpoList = new int[] { 0, 1, 2, 5 };
            }
            else if (model.Equals("Sargas"))
            {
                //GPIO InputList
                gpiList = new int[] { 0, 1 };
                //GPIO OutputList
                gpoList = new int[] { 2, 3 };
            }
            //Adding Input and output list for IZAR.
            else if (model.Equals("Izar") || model.Equals("Astra200"))
            {
                //GPIO InputList for Izar
                gpiList = new int[] { 1, 2, 3, 4 };
                //GPIO OutputList for Izar
                gpoList = new int[] { 1, 2, 3, 4 };
            }
            //software version
            softwareVersion = msgCapabRes.GeneralDeviceCapabilities.ReaderFirmwareVersion;
            //Check reader supported features
            checkSupportedFeatures();
            //Region Id
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE readerConfigResponse = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicRegionConfiguration);
                PARAM_ThingMagicRegionConfiguration par = (PARAM_ThingMagicRegionConfiguration)readerConfigResponse.Custom[0];
                regionId = (Region)par.RegionID;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }

        /// <summary>
        /// Check reader supported features for backward compatibility
        /// </summary>
        private void checkSupportedFeatures()
        {
            //Retrieve current software version
            Version currVer = new Version(softwareVersion);

            //Multiselect is supported from this version
            Version mSelVer = new Version("5.3.2.12");

            //Per antenna on time is supported from this version
            Version pAntVer = new Version("5.3.2.38");

            //Custom parameter for inventory spec ID is supported from this version
            Version cisIDVer = new Version("5.3.2.97");

            //custom Parameter are Support in this version
            Version metaDataVer = new Version("5.3.4.24");

            //Custom Stats listener is support in this version
            Version statListenerVer = new Version("5.3.4.93");

            //custom State Aware Target is support in this version
            Version stateAwareVer = new Version("5.3.4.59");

            //Checking if State Aware Target supported
            if (currVer.CompareTo(stateAwareVer) >= 0)
            {
                isStateAwareTargetMapped = true;
            }
            else
            {
                isStateAwareTargetMapped = false;
            }
            //checking if stats listener suported 
            if (currVer.CompareTo(statListenerVer) >= 0)
            {
                statListSupport = true;
            }
            else
            {
                statListSupport = false;
            }
            //checking if the metadata supported
            if (currVer.CompareTo(metaDataVer) >= 0)
            {
                customMetaDataSupport = true;
            }
            else
            {
                customMetaDataSupport = false;
            }
            //check if multiselect feature is supported
            if ((currVer.CompareTo(mSelVer)) >= 0)
            {
                multiselectSupport = true;
            }
            else
            {
                multiselectSupport = false;
            }

            //check if per antenna on time feature is supported
            if ((currVer.CompareTo(pAntVer)) >= 0)
            {
                perAntOnTimeSupport = true;
            }
            else
            {
                perAntOnTimeSupport = false;
            }

            //check if adding custom inventory spec ID is supported 
            if ((currVer.CompareTo(cisIDVer)) >= 0)
            {
                addInvSpecIDSupport = true;
            }
            else
            {
                addInvSpecIDSupport = false;
            }
        }
        #endregion ProbeHardware

        /// <summary>
        /// Read RFID tags for a fixed duration.
        /// </summary>
        /// <param name="timeout">the read timeout</param>
        /// <returns>the read tag data collection</returns>
        public override TagReadData[] Read(int timeout)
        {
            isRoAccessReportReceived = false;
            if (timeout < 0)
                throw new ArgumentOutOfRangeException(
                    "Timeout (" + timeout.ToString() + ") must be greater than or equal to 0");

            else if (timeout > 65535)
                throw new ArgumentOutOfRangeException(
                    "Timeout (" + timeout.ToString() + ") must be less than 65536");

            tagReads = new List<TagReadData>();
            EnableEventsAndReports();
            llrp.OnRoAccessReportReceived += new delegateRoAccessReport(OnRoAccessReportReceived);
            try
            {
                EnableReaderNotification();
                ReadInternal(timeout, (ReadPlan)ParamGet("/reader/read/plan"));
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }

            //Reset number of read plans to 0 at the end
            numPlans = 0;
            return tagReads.ToArray();
        }

        void OnReaderEventNotificationReceived(MSG_READER_EVENT_NOTIFICATION msg)
        {
            keepAliveTime = DateTime.Now;
            msgStartTime = DateTime.Now;
            BuildTransport(false, msg);
            PARAM_AISpecEvent aiSpecEvent = msg.ReaderEventNotificationData.AISpecEvent;
            PARAM_ROSpecEvent roSpecEvent = msg.ReaderEventNotificationData.ROSpecEvent;
            PARAM_ReaderExceptionEvent rexceptionEvent = msg.ReaderEventNotificationData.ReaderExceptionEvent;
            if (roSpecEvent != null)
            {
                uint evntNotifiedroSpecID = roSpecEvent.ROSpecID;
                UInt64 idFlag = ((UInt64)1 << (int)(evntNotifiedroSpecID - 1));
                switch (roSpecEvent.EventType)
                {
                    case ENUM_ROSpecEventType.Start_Of_ROSpec:
                        ROSpecFlags |= idFlag;
                        break;
                    case ENUM_ROSpecEventType.End_Of_ROSpec:
                        ROSpecFlags &= (~idFlag);
                        RFReportQueueEmptyEvent.Set();
                        //If reader does not send any Ro access report, set the TagQueueEmptyEvent
                        if (!isRoAccessReportReceived)
                            TagQueueEmptyEvent.Set();
                        break;
                }
                if (0 == ROSpecFlags)
                {
                    ROSpecFlagsZeroed.Set();
                    ROSpecFlagsSet.Reset();
                }
                else
                {
                    ROSpecFlagsZeroed.Reset();
                    ROSpecFlagsSet.Set();
                }
            }
            if (aiSpecEvent != null && (aiSpecEvent.EventType == ENUM_AISpecEventType.End_Of_AISpec))
            {
                // end of AISPEC
                endOfAISpec = true;
            }
            if (rexceptionEvent != null)
            {
                int val = Convert.ToInt32(rexceptionEvent.Message);
                string hexValue = val.ToString("X");
                string message = ReaderCodeException.faultCodeToMessage(Convert.ToInt32(hexValue.Substring(3), 16));
                notifyExceptionListeners(new ReaderCodeException(message, val));
            }
        }

        void OnGenericMessageReceived(LTKD.Message response)
        {
            if (response != null)
            {
                //validate the received message with sent
                msgStartTime = DateTime.Now;
                BuildTransport(false, response);
                if (!isMsgRespReceived)
                {
                    isMsgRespReceived = sentReceiveMessageValidator(msgSent, response);
                }
            }
        }

        private void ReadInternal(int timeOut, ReadPlan rp)
        {
            roSpecId = 0;
            endOfAISpec = false;
            DeleteRoSpec();
            DeleteAccessSpecs();
            TagQueueEmptyEvent.Reset();
            List<PARAM_ROSpec> roSpecList = new List<PARAM_ROSpec>();
            roSpecProtocolTable = new Hashtable();
            BuildRoSpec(rp, timeOut, roSpecList, false);
            foreach (PARAM_ROSpec roSpec in roSpecList)
            {
                if (AddRoSpec(roSpec))
                {
                    if (EnableRoSpec(roSpec.ROSpecID))
                    {
                        if (!StartRoSpec(roSpec.ROSpecID))
                            return;
                        WaitForSearchStart();
                        WaitForSearchEnd(timeOut);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
            llrp.OnRoAccessReportReceived -= new delegateRoAccessReport(OnRoAccessReportReceived);
        }

        private void WaitForSearchStart()
        {
            int timeout;
            timeout = (int)ParamGet("/reader/transportTimeout");
            /*
             * Wait for ROSpecs to start
             */
            if (false == ROSpecFlagsSet.WaitOne(timeout, false))
            {
                throw new TimeoutException("Timeout waiting for start of search");
            }
        }

        /// <summary>
        /// Wait for search end. For sync read, this method accepts readtimeout as
        /// timeout and for async read, accepts zero as timeout.
        /// </summary>
        /// <param name="readTimeout">timeout need to be added to transport 
        /// time to block the current thread until the current TagQueueEmptyEvent 
        /// or ROSpecFlagsZeroed instances receives a signal.</param>
        private void WaitForSearchEnd(int readTimeout)
        {
            int timeout;
            timeout = (int)ParamGet("/reader/transportTimeout");

            // Increase the wait time of TagQueueEmptyEvent by adding readtimeout to transport
            // timeout, when the timeout in sync read is more then default
            // transport timeout. So that WaitForSearchEnd will wait for Readtimeout + transport 
            // timeout in case of sync read before throwing TimeoutExceptions and will not wait 
            // forever when reader messages were lost.
            timeout += readTimeout;

            /*
             * Make sure all ROSpecs have stopped
             */
            if (false == ROSpecFlagsZeroed.WaitOne(timeout, false))
            {
                msgStartTime = DateTime.Now;
                while (!(Convert.ToBoolean(ROSpecFlags)))
                {
                    DateTime currentTime = DateTime.Now;
                    TimeSpan diff = currentTime - msgStartTime;
                    int diffTime = (int)diff.TotalMilliseconds;
                    if (diffTime < timeout) // timeout = (Readtimeout + transport timeout)
                    {
                        throw new TimeoutException("Timeout waiting for ROSpec flags to clear at end of search");
                    }
                }
            }

            // Wait till all the roreports are processed
            while (0 < tagReadQueue.Count)
            {
                Thread.Sleep(20);
            }
            // Wait till all the RFreports are processed
            while (0 < RFSurvyQueue.Count)
            {
                Thread.Sleep(20);
            }

            /*
             * Wait for empty tag queue first, since all
             * RO_ACCESS_REPORTS are supposed to be delivered
             * before the corresponding End_Of_ROSpec event.
             */
            if (false == TagQueueEmptyEvent.WaitOne(timeout, false))
            {
                throw new TimeoutException("Timeout waiting for tag queue to empty at end of search");
            }
            if (false == RFReportQueueEmptyEvent.WaitOne(timeout, false) && (statFlag != 0))
            {
                if (continuousReading)
                {
                    throw new TimeoutException("Timeout waiting for RF Survy queue to empty at end of search");
                }
            }
        }
        bool isRoAccessReportReceived = false;
        void OnRoAccessReportReceived(MSG_RO_ACCESS_REPORT msg)
        {
            keepAliveTime = DateTime.Now;
            msgStartTime = DateTime.Now;
            isRoAccessReportReceived = true;
            lock (isRoAccessReportsComingLock)
            {
                isRoAccessReportsComing = true;
            }
            delegateRoAccessReport del = new delegateRoAccessReport(UpdateROReport);
            delegateRoAccessReport del1 = new delegateRoAccessReport(SurveyReportData);
            del.Invoke(msg);
            del1.Invoke(msg);
        }

        /// <summary>
        /// Parsing ro access report
        /// </summary>
        /// <param name="msg">Ro access report</param>
        private void UpdateROReport(MSG_RO_ACCESS_REPORT msg)
        {
            if (msg.TagReportData == null)
            {
                //If reader does not send any Ro access report, set the TagQueueEmptyEvent 
                TagQueueEmptyEvent.Set();
                return;
            }
            //Transport listener
            BuildTransport(false, msg);
            lock (tagReadQueue)
            {
                if (processData)
                {
                    tagReadQueue.Enqueue(msg);
                    TagQueueAddedEvent.Set();
                }
            }
        }

        private void SurveyReportData(MSG_RO_ACCESS_REPORT msg)
        {
            if (msg.RFSurveyReportData == null)
            {
                //If reader does not send any Ro access report, set the RFReportQueueEmptyEvent 
                RFReportQueueEmptyEvent.Set();
                return;
            }
            //Transport listener
            BuildTransport(false, msg);
            lock (RFSurvyQueue)
            {
                if (processData)
                {
                    RFSurvyQueue.Enqueue(msg);
                    RFReportQueueAddedEvent.Set();
                }
            }
        }


        bool stopAsyncReadThread = false;

        private void ProcessRoAccessReport()
        {
            UInt64 iteration = 0;

            while (!stopAsyncReadThread)
            {
                iteration++;
                try
                {
                    MSG_RO_ACCESS_REPORT report = null;
                    lock (tagReadQueue)
                    {
                        if (0 < tagReadQueue.Count)
                        {
                            TagQueueEmptyEvent.Reset();
                            report = (MSG_RO_ACCESS_REPORT)tagReadQueue.Dequeue();
                        }
                    }
                    if (null == report)
                    {
                        while (false == TagQueueAddedEvent.WaitOne(0, false))
                        {
                            Thread.Sleep(20);
                            if (stopAsyncReadThread)
                            {
                                //Release the TagQueueEmptyEvent when close the LLRP connection
                                TagQueueEmptyEvent.Set();
                                break;
                            }
                            else
                            {
                                //Continue to wiat the for the RO_ACCESS_REPORT to add
                                continue;
                            }
                        }
                    }
                    else
                    {
                        ParseNotifyTag(report);
                    }
                }
                catch (Exception ex)
                {
                    ReaderException rex;
                    if (ex is ReaderException)
                    {
                        rex = (ReaderException)ex;
                    }
                    else
                    {
                        rex = new ReaderException("Parser thread caught exception: " + ex.Message);
                    }
                    notifyExceptionListeners(rex);
                }
            }
        }

        private void ProcessRRFReport()
        {
            UInt64 iteration = 0;

            while (!stopAsyncReadThread)
            {
                iteration++;
                try
                {
                    MSG_RO_ACCESS_REPORT report = null;
                    lock (RFSurvyQueue)
                    {
                        if (0 < RFSurvyQueue.Count)
                        {
                            RFReportQueueEmptyEvent.Reset();
                            report = (MSG_RO_ACCESS_REPORT)RFSurvyQueue.Dequeue();
                        }
                    }
                    if (null == report)
                    {
                        while (false == RFReportQueueAddedEvent.WaitOne(0, false))
                        {
                            Thread.Sleep(20);
                            if (stopAsyncReadThread)
                            {
                                //Release the RFReportQueueEmptyEvent when close the LLRP connection
                                RFReportQueueEmptyEvent.Set();
                                break;
                            }
                            else
                            {
                                //Continue to wiat the for the RO_ACCESS_REPORT to add
                                continue;
                            }
                        }
                    }
                    else
                    {
                        ParseNotifyRFReport(report);
                    }
                }
                catch (Exception ex)
                {
                    ReaderException rex;
                    if (ex is ReaderException)
                    {
                        rex = (ReaderException)ex;
                    }
                    else
                    {
                        rex = new ReaderException("Parser thread caught exception: " + ex.Message);
                    }
                    notifyExceptionListeners(rex);
                }
            }
        }

        /// <summary>
        /// Set TagQueueAddedEvent when a new report is added to the tag queue.
        /// The parser thread will wait on this event when it finds the queue empty,
        /// so it doesn't have to consume resources while waiting for a new report to appear.
        /// </summary>
        private AutoResetEvent TagQueueAddedEvent = new AutoResetEvent(false);
        private AutoResetEvent RFReportQueueAddedEvent = new AutoResetEvent(false);
        /// <summary>
        /// The parser thread sets TagQueueEmptyEvent when it finds tag queue empty
        /// and resets it when the tag queue fills again.  This event is to be used
        /// by threads that know the flow of reports to the tag queue has been shut
        /// down and wish to block until the parser thread is finished processing
        /// the outstanding ones.
        /// </summary>
        private ManualResetEvent TagQueueEmptyEvent = new ManualResetEvent(false);
        private ManualResetEvent RFReportQueueEmptyEvent = new ManualResetEvent(false);

        private byte[] EMPTY_DATA = new byte[0];
        private DateTime epochTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private void ParseNotifyRFReport(MSG_RO_ACCESS_REPORT msg)
        {
            try
            {
                PARAM_RFSurveyReportData var = msg.RFSurveyReportData[0];
                PARAM_CustomStatsValue value = (PARAM_CustomStatsValue)var.Custom[0];
                ReaderStatsReport report = new ReaderStatsReport();
                Reader.Stat.Values stat = new Stat.Values();
                stat.ANTENNA = value.AntennaParam.Antenna;
                if (value.ConnectedAntennaList != null)
                {
                    stat.totalAntennaCount = (value.ConnectedAntennaList.connectedAntennas.Count / 2);
                    List<uint> data1 = new List<uint>();
                    for (int i = 0; i < value.ConnectedAntennaList.connectedAntennas.Count; i++)
                    {
                        i++;
                        if (value.ConnectedAntennaList.connectedAntennas[i] == 1)
                        {
                            data1.Add(value.ConnectedAntennaList.connectedAntennas[(i - 1)]);
                        }
                    }
                    stat.CONNECTEDANTENNA = data1.ToArray();
                }
                if (value.FrequencyParam != null)
                {
                    stat.FREQUENCY = value.FrequencyParam.Frequency;
                }
                if (value.ProtocolParam != null)
                {
                    stat.PROTOCOL = (TagProtocol)value.ProtocolParam.Protocol;
                }
                stat.TEMPERATURE = (SByte)value.TemperatureParam.Temperature;

                if (value.perAntennaStatsList != null)
                {
                    Stat.PerAntennaValues otherval = null;
                    List<Stat.PerAntennaValues> tempdata = new List<Stat.PerAntennaValues>();
                    for (int i = 0; i < value.perAntennaStatsList.Length; i++)
                    {
                        otherval = new Stat.PerAntennaValues();
                        if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableAntennaPorts) == statFlag)
                            otherval.Antenna = value.perAntennaStatsList[i].antenna;
                        if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableNoiseFloorSearchRxTxWithTxOn) == statFlag)
                            otherval.NoiseFloor = value.perAntennaStatsList[i].NoiseFloorParam.noiseFloor;
                        if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableRFOnTime) == statFlag)
                            otherval.RfOnTime = value.perAntennaStatsList[i].RFOntimeParam.rfOntime;
                        tempdata.Add(otherval);
                    }
                    stat.PERANTENNA = tempdata;
                }
                stat.VALID = (Stat.StatsFlag)value.StatsEnable;
                report.STATS = stat;
                OnStatsRead(report);
            }
            catch (Exception)
            {
                //Console.WriteLine("OnStatsRead"+ex);
                //throw;
            }
            finally
            {
                RFReportQueueEmptyEvent.Set();
            }
        }

        private void ParseNotifyTag(MSG_RO_ACCESS_REPORT msg)
        {
            if (null == tagReads)
            {
                tagReads = new List<TagReadData>();
            }
            TagReadData tag = null;
            if (null != msg)
            {
                for (int i = 0; i < msg.TagReportData.Length; i++)
                {
                    try
                    {
                        tag = new TagReadData();
                        if (msg.TagReportData[i].EPCParameter.Count > 0)
                        {
                            string epc;
                            // reports come in two flavors.  Get the right flavor
                            if (msg.TagReportData[i].EPCParameter[0].GetType() == typeof(PARAM_EPC_96))
                            {
                                epc = ((PARAM_EPC_96)(msg.TagReportData[i].EPCParameter[0])).EPC.ToHexString();
                            }
                            else
                            {
                                epc = ((PARAM_EPCData)(msg.TagReportData[i].EPCParameter[0])).EPC.ToHexString();
                            }
                            TagData td = new TagData(ByteFormat.FromHex(epc));
                            TagProtocol tagProtocol = 0;

                            if (perAntOnTimeSupport == true)
                            {
                                // Currently protocol ID is coming as 2nd custom parameter
                                PARAM_ThingMagicCustomProtocolID protocol = new PARAM_ThingMagicCustomProtocolID();
                                if (customMetaDataSupport && metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                {
                                    for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                    {
                                        if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicCustomProtocolID)
                                        {
                                            protocol = (PARAM_ThingMagicCustomProtocolID)(msg.TagReportData[i].Custom[j]);
                                        }
                                    }
                                }
                                else
                                {
                                    for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                    {
                                        if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicCustomProtocolID)
                                        {
                                            protocol = (PARAM_ThingMagicCustomProtocolID)(msg.TagReportData[i].Custom[j]);
                                        }
                                    }
                                }
                                ENUM_ThingMagicCustomProtocol protocolID = protocol.ProtocolId;
                                tagProtocol = parseThingmagicCustomProtocol(Convert.ToInt32(protocolID));
                            }
                            else
                            {
                                //Match the received rospec id with the rospec id stored in the hashtable at the time of setting readplan
                                if (roSpecProtocolTable.ContainsKey(msg.TagReportData[i].ROSpecID.ROSpecID))
                                {
                                    tagProtocol = (TagProtocol)roSpecProtocolTable[msg.TagReportData[i].ROSpecID.ROSpecID];
                                }
                            }

                            if (TagProtocol.GEN2.Equals(tagProtocol))
                            {
                                //Get crc and pc bits
                                UNION_AirProtocolTagData tagdata = msg.TagReportData[i].AirProtocolTagData;
                                td = new Gen2.TagData(ByteFormat.FromHex(epc), ByteConv.EncodeU16(((PARAM_C1G2_CRC)tagdata[1]).CRC), ByteConv.EncodeU16(((PARAM_C1G2_PC)tagdata[0]).PC_Bits));
                            }
                            else if (TagProtocol.ISO180006B.Equals(tagProtocol))
                            {
                                td = new Iso180006b.TagData(ByteFormat.FromHex(epc));
                            }
                            else if (TagProtocol.IPX64.Equals(tagProtocol))
                            {
                                td = new Ipx64.TagData(ByteFormat.FromHex(epc));
                            }
                            else if (TagProtocol.IPX256.Equals(tagProtocol))
                            {
                                td = new Ipx256.TagData(ByteFormat.FromHex(epc));
                            }
                            else if (TagProtocol.ATA.Equals(tagProtocol))
                            {
                                td = new Ata.TagData(ByteFormat.FromHex(epc));
                            }
                            else
                            {
                                td = new TagData(ByteFormat.FromHex(epc));
                            }
                            tag.Reader = this;
                            tag._tagData = td;
                            tag.metaDataFlags = (SerialReader.TagMetadataFlag)metadataflag;
                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataAntID) == ENUM_ThingMagicCustomMetadataFlag.MetadataAntID) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                            {
                                tag._antenna = (int)msg.TagReportData[i].AntennaID.AntennaID;
                            }

                            UInt64 usSinceEpoch = msg.TagReportData[i].LastSeenTimestampUTC.Microseconds;
                            DateTime currentTime = epochTime.AddMilliseconds(usSinceEpoch / 1000);
                            tag._baseTime = currentTime.ToLocalTime();
                            tag._readOffset = 0;
                            // Since Spruce release firmware doesn't support phase, there won't be PARAM_ThingMagicTagReportContentSelector 
                            // custom paramter in ROReportSpec
                            string[] ver = softwareVersion.Split('.');
                            if (((Convert.ToInt32(ver[0]) == 4) && (Convert.ToInt32(ver[1]) >= 17)) ||
                                (Convert.ToInt32(ver[0]) > 4))
                            {
                                if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataPhase) == ENUM_ThingMagicCustomMetadataFlag.MetadataPhase) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                {
                                    for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                    {
                                        if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicRFPhase)
                                        {
                                            tag._phase = Convert.ToInt32(((PARAM_ThingMagicRFPhase)msg.TagReportData[i].Custom[j]).Phase);
                                        }
                                    }
                                }


                                if (tagProtocol == TagProtocol.GEN2)
                                {
                                    Gen2.TagReadData gen2 = new Gen2.TagReadData();

                                    int gen2TargetResponse = 2;
                                    //if ((metadataflag | ENUM_ThingMagicCustomMetadataFlag.MetadataGPIOStatus) == ENUM_ThingMagicCustomMetadataFlag.MetadataGPIOStatus)
                                    if (customMetaDataSupport)
                                    {
                                        if ((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGPIOStatus) == (ENUM_ThingMagicCustomMetadataFlag.MetadataGPIOStatus) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                        {

                                            PARAM_ThingMagicMetadataGPIO gpi = new PARAM_ThingMagicMetadataGPIO();
                                            for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                            {
                                                if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicMetadataGPIO)
                                                {
                                                    gpi = ((PARAM_ThingMagicMetadataGPIO)msg.TagReportData[i].Custom[j]);
                                                }
                                            }
                                            PARAM_GPIOStatus[] gpio = gpi.GPIOStatus;
                                            List<GpioPin> gpioPins = new List<GpioPin>();
                                            if (gpio != null)
                                            {
                                                foreach (PARAM_GPIOStatus gp in gpio)
                                                {
                                                    int id = gp.id;
                                                    bool status = gp.Status;
                                                    bool direction = gp.Direction;
                                                    GpioPin gpioPin = new GpioPin(id, status);
                                                    gpioPins.Add(gpioPin);
                                                }
                                            }
                                            tag._GPIO = gpioPins.ToArray();
                                        }


                                        if (msg.TagReportData[i].Custom.Length >= 2)
                                        {
                                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Q) == ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Q) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                            {
                                                PARAM_ThingMagicMetadataGen2 ge2Q = new PARAM_ThingMagicMetadataGen2();
                                                for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                                {
                                                    if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicMetadataGen2)
                                                    {
                                                        ge2Q = ((PARAM_ThingMagicMetadataGen2)msg.TagReportData[i].Custom[j]);
                                                    }
                                                }
                                                gen2.Q.InitialQ = ge2Q.Gen2QResponse.QValue;
                                            }

                                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGen2LF) == ENUM_ThingMagicCustomMetadataFlag.MetadataGen2LF) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                            {
                                                PARAM_ThingMagicMetadataGen2 gen2LF = new PARAM_ThingMagicMetadataGen2();
                                                for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                                {
                                                    if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicMetadataGen2)
                                                    {
                                                        gen2LF = ((PARAM_ThingMagicMetadataGen2)msg.TagReportData[i].Custom[j]);
                                                    }
                                                }
                                                gen2._lf = (Gen2.LinkFrequency)gen2LF.Gen2LFResponse.LFValue;
                                            }

                                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Target) == ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Target) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                            {
                                                PARAM_ThingMagicMetadataGen2 gen2targ = new PARAM_ThingMagicMetadataGen2();
                                                for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                                {
                                                    if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicMetadataGen2)
                                                    {
                                                        gen2targ = ((PARAM_ThingMagicMetadataGen2)msg.TagReportData[i].Custom[j]);
                                                    }
                                                }
                                                gen2TargetResponse = Convert.ToByte(gen2targ.Gen2TargetResponse.TargetValue);
                                                switch (gen2TargetResponse)
                                                {
                                                    case 0x00:
                                                        gen2._target = Gen2.Target.A;
                                                        break;
                                                    case 0x01:
                                                        gen2._target = Gen2.Target.B;
                                                        break;
                                                }
                                            }

                                        }

                                        if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataData) == ENUM_ThingMagicCustomMetadataFlag.MetadataData) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                                        {
                                            if (msg.TagReportData[i].Custom.Length >= 5)
                                            {
                                                PARAM_ThingMagicCustomTagopResponse custtagop = new PARAM_ThingMagicCustomTagopResponse();
                                                for (int j = 0; j < msg.TagReportData[i].Custom.Count; j++)
                                                {
                                                    if (msg.TagReportData[i].Custom[j] is PARAM_ThingMagicCustomTagopResponse)
                                                    {
                                                        custtagop = ((PARAM_ThingMagicCustomTagopResponse)msg.TagReportData[i].Custom[j]);
                                                    }
                                                }
                                                PARAM_TagopByteStreamParam tagOpByteStream = (PARAM_TagopByteStreamParam)custtagop.TagopByteStreamParam;
                                                tag._data = tagOpByteStream.ByteStream.ToArray();
                                            }
                                            else
                                            {
                                                for (int j = 0; j < msg.TagReportData[i].Custom.Length; j++)
                                                {
                                                    try
                                                    //if()
                                                    {
                                                        PARAM_ThingMagicCustomTagopResponse custtagop = ((PARAM_ThingMagicCustomTagopResponse)msg.TagReportData[i].Custom[j]);
                                                        PARAM_TagopByteStreamParam tagOpByteStream = (PARAM_TagopByteStreamParam)custtagop.TagopByteStreamParam;
                                                        tag._data = tagOpByteStream.ByteStream.ToArray();
                                                    }
                                                    catch (Exception)
                                                    {
                                                        continue;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    tag.prd = gen2;
                                }
                            }

                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataRSSI) == ENUM_ThingMagicCustomMetadataFlag.MetadataRSSI) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                            {
                                tag.Rssi = Convert.ToInt32(msg.TagReportData[i].PeakRSSI.PeakRSSI.ToString());
                            }
                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataReadCount) == ENUM_ThingMagicCustomMetadataFlag.MetadataReadCount) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                            {
                                tag.ReadCount = msg.TagReportData[i].TagSeenCount.TagCount;
                            }


                            if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataFrequency) == ENUM_ThingMagicCustomMetadataFlag.MetadataFrequency) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                            {
                                int chIndex = Convert.ToInt32(msg.TagReportData[i].ChannelIndex.ChannelIndex);
                                List<uint> freq = frequencyHopTable[0].Frequency.data;
                                tag._frequency = (chIndex > 0) ? Convert.ToInt32(freq[chIndex - 1]) : 0;
                            }
                            UNION_AccessCommandOpSpecResult opSpecResult = msg.TagReportData[i].AccessCommandOpSpecResult;
                            //tag._data = EMPTY_DATA;
                            // Use try-finally to to keep failed tagops from preventing report of TagReadData
                            try
                            {
                                if (null != opSpecResult)
                                {
                                    if (opSpecResult.Count > 0)
                                    {
                                        ParseTagOpSpecResultType(opSpecResult, ref tag);
                                    }
                                }
                            }
                            finally
                            {
                                if (continuousReading)
                                {
                                    OnTagRead(tag);
                                }
                                else
                                {
                                    tagReads.Add(tag);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ReaderException rx;
                        if (ex is ReaderException)
                        {
                            rx = (ReaderException)ex;
                        }
                        else
                        {
                            rx = new ReaderException(ex.ToString());
                        }
                        //Release the TagQueueEmptyEvent when parsing exception raised
                        TagQueueEmptyEvent.Set();
                        ReadExceptionPublisher expub = new ReadExceptionPublisher(this, rx);

                        Thread trd = new Thread(expub.OnReadException);
                        trd.Name = "OnReadException";
                        trd.Start();
                    }
                    finally
                    {
                        tag = null;
                    }
                }
                TagQueueEmptyEvent.Set();
            }
        }

        // Method to parse the thingmagic custom protocol id and return TagProtocol.
        private TagProtocol parseThingmagicCustomProtocol(int protocolID)
        {
            TagProtocol protocol = TagProtocol.NONE;
            switch (protocolID)
            {
                case 1:
                    protocol = TagProtocol.GEN2;
                    break;
                case 2:
                    protocol = TagProtocol.ISO180006B;
                    break;
                case 3:
                    protocol = TagProtocol.IPX64;
                    break;
                case 4:
                    protocol = TagProtocol.IPX256;
                    break;
                case 5:
                    protocol = TagProtocol.ATA;
                    break;
                default:
                    break;
            }
            return protocol;
        }

        /// <summary>
        /// Parse tag opspec result type
        /// </summary>
        /// <param name="opSpecResult"></param>
        /// <param name="tag"></param>
        private void ParseTagOpSpecResultType(UNION_AccessCommandOpSpecResult opSpecResult, ref TagReadData tag)
        {
            // TODO: Generalize ReaderCodeExceptions to cover all types of readers, not just Serial
            if (opSpecResult[0].GetType() == typeof(PARAM_C1G2ReadOpSpecResult))
            {
                switch (((PARAM_C1G2ReadOpSpecResult)opSpecResult[0]).Result)
                {
                    case ENUM_C1G2ReadResultType.Success:
                        tag._data = ByteConv.ConvertFromUshortArray(((PARAM_C1G2ReadOpSpecResult)opSpecResult[0]).ReadData.data.ToArray());
                        tag.dataLength = tag.Data.Length;
                        tagOpResponse = ((PARAM_C1G2ReadOpSpecResult)opSpecResult[0]).ReadData.data.ToArray();
                        break;
                    case ENUM_C1G2ReadResultType.No_Response_From_Tag:
                        throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                    case ENUM_C1G2ReadResultType.Nonspecific_Reader_Error:
                        throw new ReaderException("Non-specific reader error");
                    case ENUM_C1G2ReadResultType.Nonspecific_Tag_Error:
                        throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                    case ENUM_C1G2ReadResultType.Memory_Locked_Error:
                        throw new ReaderCodeException("Tag memory locked error", FAULT_GEN2_PROTOCOL_MEMORY_LOCKED_Exception.StatusCode);
                    case ENUM_C1G2ReadResultType.Memory_Overrun_Error:
                        throw new ReaderCodeException("Tag memory overrun error", FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception.StatusCode);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_C1G2WriteOpSpecResult))
            {
                switch (((PARAM_C1G2WriteOpSpecResult)opSpecResult[0]).Result)
                {
                    case ENUM_C1G2WriteResultType.Success:
                        break;
                    case ENUM_C1G2WriteResultType.Insufficient_Power:
                        throw new ReaderCodeException("Insufficient power", FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception.StatusCode);
                    case ENUM_C1G2WriteResultType.Tag_Memory_Locked_Error:
                        throw new ReaderCodeException("Tag memory locked error", FAULT_GEN2_PROTOCOL_MEMORY_LOCKED_Exception.StatusCode);
                    case ENUM_C1G2WriteResultType.No_Response_From_Tag:
                        throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                    case ENUM_C1G2WriteResultType.Nonspecific_Reader_Error:
                        throw new ReaderException("Non-specific reader error");
                    case ENUM_C1G2WriteResultType.Nonspecific_Tag_Error:
                        throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                    case ENUM_C1G2WriteResultType.Tag_Memory_Overrun_Error:
                        throw new ReaderCodeException("Tag memory overrun error", FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception.StatusCode);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_C1G2KillOpSpecResult))
            {
                switch (((PARAM_C1G2KillOpSpecResult)opSpecResult[0]).Result)
                {
                    case ENUM_C1G2KillResultType.Success:
                        break;
                    case ENUM_C1G2KillResultType.Insufficient_Power:
                        throw new ReaderCodeException("Insufficient power", FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception.StatusCode);
                    case ENUM_C1G2KillResultType.No_Response_From_Tag:
                        throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                    case ENUM_C1G2KillResultType.Nonspecific_Reader_Error:
                        throw new ReaderException("Non-specific reader error");
                    case ENUM_C1G2KillResultType.Nonspecific_Tag_Error:
                        throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                    case ENUM_C1G2KillResultType.Zero_Kill_Password_Error:
                        throw new ReaderException("Zero kill password error");
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_C1G2BlockWriteOpSpecResult))
            {
                switch (((PARAM_C1G2BlockWriteOpSpecResult)opSpecResult[0]).Result)
                {
                    case ENUM_C1G2BlockWriteResultType.Success:
                        break;
                    case ENUM_C1G2BlockWriteResultType.Insufficient_Power:
                        throw new ReaderCodeException("Insufficient power", FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception.StatusCode);
                    case ENUM_C1G2BlockWriteResultType.Tag_Memory_Locked_Error:
                        throw new ReaderCodeException("Tag memory locked error", FAULT_GEN2_PROTOCOL_MEMORY_LOCKED_Exception.StatusCode);
                    case ENUM_C1G2BlockWriteResultType.No_Response_From_Tag:
                        throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                    case ENUM_C1G2BlockWriteResultType.Nonspecific_Reader_Error:
                        throw new ReaderException("Non-specific reader error");
                    case ENUM_C1G2BlockWriteResultType.Nonspecific_Tag_Error:
                        throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                    case ENUM_C1G2BlockWriteResultType.Tag_Memory_Overrun_Error:
                        throw new ReaderCodeException("Tag memory overrun error", FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception.StatusCode);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_C1G2LockOpSpecResult))
            {
                switch (((PARAM_C1G2LockOpSpecResult)opSpecResult[0]).Result)
                {
                    case ENUM_C1G2LockResultType.Success:
                        break;
                    case ENUM_C1G2LockResultType.Insufficient_Power:
                        throw new ReaderCodeException("Insufficient power", FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception.StatusCode);
                    case ENUM_C1G2LockResultType.No_Response_From_Tag:
                        throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                    case ENUM_C1G2LockResultType.Nonspecific_Reader_Error:
                        throw new ReaderException("Non-specific reader error");
                    case ENUM_C1G2LockResultType.Nonspecific_Tag_Error:
                        throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_C1G2BlockEraseOpSpecResult))
            {
                switch (((PARAM_C1G2BlockEraseOpSpecResult)opSpecResult[0]).Result)
                {
                    case ENUM_C1G2BlockEraseResultType.Success:
                        break;
                    case ENUM_C1G2BlockEraseResultType.Insufficient_Power:
                        throw new ReaderCodeException("Insufficient power", FAULT_GEN2_PROTOCOL_INSUFFICIENT_POWER_Exception.StatusCode);
                    case ENUM_C1G2BlockEraseResultType.No_Response_From_Tag:
                        throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                    case ENUM_C1G2BlockEraseResultType.Nonspecific_Reader_Error:
                        throw new ReaderException("Non-specific reader error");
                    case ENUM_C1G2BlockEraseResultType.Nonspecific_Tag_Error:
                        throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                    case ENUM_C1G2BlockEraseResultType.Tag_Memory_Locked_Error:
                        throw new ReaderCodeException("Tag memory locked error", FAULT_GEN2_PROTOCOL_MEMORY_LOCKED_Exception.StatusCode);
                    case ENUM_C1G2BlockEraseResultType.Tag_Memory_Overrun_Error:
                        throw new ReaderCodeException("Tag memory overrun error", FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception.StatusCode);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicBlockPermalockOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicBlockPermalockOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicBlockPermalockOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tagOpResponse = ByteConv.ConvertFromUshortArray(((PARAM_ThingMagicBlockPermalockOpSpecResult)opSpecResult[0]).PermalockStatus.data.ToArray());
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicWriteTagOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicWriteTagOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicHiggs2PartialLoadImageOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicHiggs2PartialLoadImageOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicHiggs2FullLoadImageOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicHiggs2FullLoadImageOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2XResetReadProtectOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2XResetReadProtectOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicHiggs3FastLoadImageOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicHiggs3FastLoadImageOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicHiggs3LoadImageOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicHiggs3LoadImageOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicHiggs3BlockReadLockOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicHiggs3BlockReadLockOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2ICalibrateOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2ICalibrateOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPG2ICalibrateOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tagOpResponse = ((PARAM_ThingMagicNXPG2ICalibrateOpSpecResult)opSpecResult[0]).CalibrateData.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2IChangeConfigOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2IChangeConfigOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPG2IChangeConfigOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    Gen2.NXP.G2I.ConfigWord word = new Gen2.NXP.G2I.ConfigWord();
                    tagOpResponse = word.GetConfigWord(((PARAM_ThingMagicNXPG2IChangeConfigOpSpecResult)opSpecResult[0]).ConfigData);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2IChangeEASOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2IChangeEASOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2IResetReadProtectOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2IResetReadProtectOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2ISetReadProtectOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2ISetReadProtectOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2XCalibrateOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2XCalibrateOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPG2XCalibrateOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tagOpResponse = ((PARAM_ThingMagicNXPG2XCalibrateOpSpecResult)opSpecResult[0]).CalibrateData.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2XChangeEASOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2XChangeEASOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2XSetReadProtectOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2XSetReadProtectOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicImpinjMonza4QTReadWriteOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicImpinjMonza4QTReadWriteOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicImpinjMonza4QTReadWriteOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    Gen2.Impinj.Monza4.QTPayload qtPayload = new Gen2.Impinj.Monza4.QTPayload();
                    int res = ((PARAM_ThingMagicImpinjMonza4QTReadWriteOpSpecResult)opSpecResult[0]).Payload;
                    //Construct QTPayload
                    if ((res & 0x8000) != 0)
                    {
                        qtPayload.QTSR = true;
                    }
                    if ((res & 0x4000) != 0)
                    {
                        qtPayload.QTMEM = true;
                    }
                    tagOpResponse = qtPayload;
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2IEASAlarmOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2IEASAlarmOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPG2IEASAlarmOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tagOpResponse = ((PARAM_ThingMagicNXPG2IEASAlarmOpSpecResult)opSpecResult[0]).EASAlarmCode;
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPG2XEASAlarmOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPG2XEASAlarmOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPG2XEASAlarmOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tagOpResponse = ((PARAM_ThingMagicNXPG2XEASAlarmOpSpecResult)opSpecResult[0]).EASAlarmCode;
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicISO180006BReadOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicISO180006BReadOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicISO180006BReadOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tag._data = ((PARAM_ThingMagicISO180006BReadOpSpecResult)opSpecResult[0]).ReadData.ToArray();
                    tagOpResponse = ((PARAM_ThingMagicISO180006BReadOpSpecResult)opSpecResult[0]).ReadData.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicISO180006BLockOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicISO180006BLockOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicISO180006BWriteOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicISO180006BWriteOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AGetBatteryLevelOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AGetBatteryLevelOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900AGetBatteryLevelOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tag._data = ByteConv.EncodeU16(((PARAM_ThingMagicIDSSL900AGetBatteryLevelOpSpecResult)opSpecResult[0]).ThingMagicIDSBatteryLevel.reply);
                    Gen2.IDS.SL900A.BatteryLevelReading batteryLevel = new Gen2.IDS.SL900A.BatteryLevelReading(((PARAM_ThingMagicIDSSL900AGetBatteryLevelOpSpecResult)opSpecResult[0]).ThingMagicIDSBatteryLevel.reply);
                    tagOpResponse = batteryLevel;
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ASensorValueOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ASensorValueOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900ASensorValueOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tag._data = ByteConv.EncodeU16(((PARAM_ThingMagicIDSSL900ASensorValueOpSpecResult)opSpecResult[0]).reply);
                    Gen2.IDS.SL900A.SensorReading sensorReading = new Gen2.IDS.SL900A.SensorReading(((PARAM_ThingMagicIDSSL900ASensorValueOpSpecResult)opSpecResult[0]).reply);
                    tagOpResponse = sensorReading;
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ASetCalibrationDataOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ASetCalibrationDataOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AGetCalibrationDataOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AGetCalibrationDataOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900AGetCalibrationDataOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    PARAM_ThingMagicIDSCalibrationData data = ((PARAM_ThingMagicIDSSL900AGetCalibrationDataOpSpecResult)opSpecResult[0]).ThingMagicIDSCalibrationData;
                    LTKD.ByteArray calibrationValue = data.calibrationValueByteStream;
                    tag._data = calibrationValue.ToArray();
                    tagOpResponse = new Gen2.IDS.SL900A.CalSfe(calibrationValue.ToArray(), 0);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AEndLogOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AEndLogOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AInitializeOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AInitializeOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ALogStateOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ALogStateOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900ALogStateOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray logStateByteArray = ((PARAM_ThingMagicIDSSL900ALogStateOpSpecResult)opSpecResult[0]).LogStateByteStream;
                    tag._data = logStateByteArray.ToArray();
                    tagOpResponse = new Gen2.IDS.SL900A.LogState(logStateByteArray.ToArray());
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ASetLogModeOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ASetLogModeOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AStartLogOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AStartLogOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ASetSFEParamsOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ASetSFEParamsOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AGetMeasurementSetupOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AGetMeasurementSetupOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900AGetMeasurementSetupOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray measurementByteArray = ((PARAM_ThingMagicIDSSL900AGetMeasurementSetupOpSpecResult)opSpecResult[0]).measurementByteStream;
                    tag._data = measurementByteArray.ToArray();
                    tagOpResponse = new Gen2.IDS.SL900A.MeasurementSetupData(measurementByteArray.ToArray(), 0);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AAccessFIFOReadOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AAccessFIFOReadOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900AAccessFIFOReadOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray readPayLoad = ((PARAM_ThingMagicIDSSL900AAccessFIFOReadOpSpecResult)opSpecResult[0]).readPayLoad;
                    tag._data = readPayLoad.ToArray();
                    tagOpResponse = readPayLoad.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AAccessFIFOWriteOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AAccessFIFOWriteOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900AAccessFIFOStatusOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900AAccessFIFOStatusOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicIDSSL900AAccessFIFOStatusOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    tag._data = ByteConv.EncodeU16(Convert.ToUInt16(((PARAM_ThingMagicIDSSL900AAccessFIFOStatusOpSpecResult)opSpecResult[0]).FIFOStatusRawByte));
                    tagOpResponse = new Gen2.IDS.SL900A.FifoStatus(((PARAM_ThingMagicIDSSL900AAccessFIFOStatusOpSpecResult)opSpecResult[0]).FIFOStatusRawByte);
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ASetLogLimitsOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ASetLogLimitsOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSetShelfLifeOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSetShelfLifeOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicIDSSL900ASetPasswordOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicIDSSL900ASetPasswordOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVActivateSecureModeOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVActivateSecureModeOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVActivateSecureModeOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray activateSecureModeValue = ((PARAM_ThingMagicDenatranIAVActivateSecureModeOpSpecResult)opSpecResult[0]).ActivateSecureModeByteStream;
                    tag._data = activateSecureModeValue.ToArray();
                    tagOpResponse = activateSecureModeValue.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVActivateSiniavModeOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVActivateSiniavModeOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVActivateSiniavModeOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray activateSiniavModeValue = ((PARAM_ThingMagicDenatranIAVActivateSiniavModeOpSpecResult)opSpecResult[0]).ActivateSiniavModeByteStream;
                    tag._data = activateSiniavModeValue.ToArray();
                    tagOpResponse = activateSiniavModeValue.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVAuthenticateOBUOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVAuthenticateOBUOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVAuthenticateOBUOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray authenticateOBUValue = ((PARAM_ThingMagicDenatranIAVAuthenticateOBUOpSpecResult)opSpecResult[0]).AuthenitcateOBUByteStream;
                    tag._data = authenticateOBUValue.ToArray();
                    tagOpResponse = authenticateOBUValue.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1OpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1OpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1OpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray authenticateFullPass1Value = ((PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1OpSpecResult)opSpecResult[0]).OBUAuthenticateFullPass1ByteStream;
                    tag._data = authenticateFullPass1Value.ToArray();
                    tagOpResponse = authenticateFullPass1Value.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2OpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2OpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2OpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray authenticateFullPass2Value = ((PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2OpSpecResult)opSpecResult[0]).OBUAuthenticateFullPass2ByteStream;
                    tag._data = authenticateFullPass2Value.ToArray();
                    tagOpResponse = authenticateFullPass2Value.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVOBUAuthenticateIDOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVOBUAuthenticateIDOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVOBUAuthenticateIDOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray authenticateIDValue = ((PARAM_ThingMagicDenatranIAVOBUAuthenticateIDOpSpecResult)opSpecResult[0]).OBUAuthenticateIDByteStream;
                    tag._data = authenticateIDValue.ToArray();
                    tagOpResponse = authenticateIDValue.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVOBUReadFromMemMapOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVOBUReadFromMemMapOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVOBUReadFromMemMapOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray readMemoryMapValue = ((PARAM_ThingMagicDenatranIAVOBUReadFromMemMapOpSpecResult)opSpecResult[0]).OBUReadMemoryMapByteStream;
                    tag._data = readMemoryMapValue.ToArray();
                    tagOpResponse = readMemoryMapValue.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicDenatranIAVOBUWriteToMemMapOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicDenatranIAVOBUWriteToMemMapOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicDenatranIAVOBUWriteToMemMapOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray writeMemoryMapValue = ((PARAM_ThingMagicDenatranIAVOBUWriteToMemMapOpSpecResult)opSpecResult[0]).OBUWriteMemoryMapByteStream;
                    tag._data = writeMemoryMapValue.ToArray();
                    tagOpResponse = writeMemoryMapValue.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPAuthenticationOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPAuthenticationOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPAuthenticationOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray authenticationByteStream = ((PARAM_ThingMagicNXPAuthenticationOpSpecResult)opSpecResult[0]).NXPAuthenticationByteStream;
                    tag._data = authenticationByteStream.ToArray();
                    tagOpResponse = authenticationByteStream.ToArray();
                }
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPUntraceableOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPUntraceableOpSpecResult)opSpecResult[0]).Result);
            }
            else if (opSpecResult[0].GetType() == typeof(PARAM_ThingMagicNXPReadbufferOpSpecResult))
            {
                ParseCustomTagOpSpecResultType(((PARAM_ThingMagicNXPReadbufferOpSpecResult)opSpecResult[0]).Result);
                if (((PARAM_ThingMagicNXPReadbufferOpSpecResult)opSpecResult[0]).Result.Equals(ENUM_ThingMagicCustomTagOpSpecResultType.Success))
                {
                    LTKD.ByteArray readBufferByteStream = ((PARAM_ThingMagicNXPReadbufferOpSpecResult)opSpecResult[0]).NXPReadbufferByteStream;
                    tag._data = readBufferByteStream.ToArray();
                    tagOpResponse = readBufferByteStream.ToArray();
                }
            }
        }

        /// <summary>
        /// Parse custom tag op spec result type
        /// </summary>
        /// <param name="result"></param>
        private void ParseCustomTagOpSpecResultType(ENUM_ThingMagicCustomTagOpSpecResultType result)
        {
            switch (result)
            {
                case ENUM_ThingMagicCustomTagOpSpecResultType.Success:
                    break;
                case ENUM_ThingMagicCustomTagOpSpecResultType.No_Response_From_Tag:
                    throw new ReaderCodeException("No response from tag", FAULT_PROTOCOL_NO_DATA_READ_Exception.StatusCode);
                case ENUM_ThingMagicCustomTagOpSpecResultType.Nonspecific_Reader_Error:
                    throw new ReaderException("Non-specific reader error");
                case ENUM_ThingMagicCustomTagOpSpecResultType.Nonspecific_Tag_Error:
                    throw new ReaderCodeException("Non-specific tag error", FAULT_GENERAL_TAG_ERROR_Exception.StatusCode);
                case ENUM_ThingMagicCustomTagOpSpecResultType.Tag_Memory_Overrun_Error:
                    throw new ReaderCodeException("Tag memory overrun error", FAULT_GEN2_PROTOCOL_MEMORY_OVERRUN_BAD_PC_Exception.StatusCode);
                case ENUM_ThingMagicCustomTagOpSpecResultType.Unsupported_Operation:
                    throw new FeatureNotSupportedException("Unsupported operation");
                case ENUM_ThingMagicCustomTagOpSpecResultType.Gen2V2_Authentication_Fail:
                    throw new ReaderCodeException("Authentication failed with specified key", FAULT_GEN2_PROTOCOL_V2_AUTHEN_FAILED_Exception.StatusCode);
                case ENUM_ThingMagicCustomTagOpSpecResultType.Gen2V2_Untrace_Fail:
                    throw new ReaderCodeException("Untrace operation failed", FAULT_GEN2_PROTOCOL_V2_UNTRACE_FAILED_Exception.StatusCode);
            }
        }

        /// <summary>
        /// Start reading RFID tags in the background. The tags found will be
        /// passed to the registered read listeners, and any exceptions that
        /// occur during reading will be passed to the registered exception
        /// listeners. Reading will continue until stopReading() is called.
        /// </summary>
        public override void StartReading()
        {
            EnableEventsAndReports();
            llrp.OnRoAccessReportReceived += new delegateRoAccessReport(OnRoAccessReportReceived);
            continuousReading = true;
            isRoAccessReportReceived = false;
            roSpecId = 0;
            roSpecList = new List<PARAM_ROSpec>();
            roSpecProtocolTable = new Hashtable();
            int asyncOnTime = Convert.ToInt32(ParamGet("/reader/read/asyncOnTime"));
            try
            {
                ReadPlan rp = (ReadPlan)ParamGet("/reader/read/plan");
                DeleteRoSpec();
                DeleteAccessSpecs();
                TagQueueEmptyEvent.Reset();
                //Register events
                EnableReaderNotification();
                //Build ro spec
                BuildRoSpec(rp, asyncOnTime, roSpecList, false);
                if (roSpecList.Count != 0)
                {
                    foreach (PARAM_ROSpec roSpec in roSpecList)
                    {
                        if (AddRoSpec(roSpec))
                        {
                            if (EnableRoSpec(roSpec.ROSpecID))
                            {
                                if (roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType != ENUM_ROSpecStartTriggerType.Periodic)
                                {
                                    if (!StartRoSpec(roSpec.ROSpecID))
                                    {
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }

        /// <summary>
        /// Stop reading RFID tags in the background.
        /// </summary>
        public override void StopReading()
        {
            if ((null != roSpecList) && (continuousReading))
            {
                foreach (PARAM_ROSpec roSpec in roSpecList)
                {
                    if (roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType != ENUM_ROSpecStartTriggerType.Periodic)
                    {
                        StopRoSpec(roSpec.ROSpecID);
                    }
                    else
                    {
                        DisableROSpec(roSpec.ROSpecID);
                    }
                }
                WaitForSearchEnd(0);
                llrp.OnRoAccessReportReceived -= new delegateRoAccessReport(OnRoAccessReportReceived);
                continuousReading = false;
                numPlans = 0;
            }
        }

        #region Firmware Methods

        #region FirmwareLoad
        /// <summary>
        /// Loads firmware on the Reader.
        /// </summary>
        /// <param name="firmware">Firmware IO stream</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void FirmwareLoad(Stream firmware)
        {
            FirmwareLoad(firmware, null);
        }

        /// <summary>
        /// Loads firmware on the Reader.
        /// </summary>
        /// <param name="firmware">Firmware IO stream</param>
        /// <param name="flOptions">firmware load options</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void FirmwareLoad(Stream firmware, FirmwareLoadOptions flOptions)
        {
            try
            {
                isFirmwareLoadInProgress = true;
                ReaderUtil.FirmwareLoadUtil(this, firmware, (FixedReaderFirmwareLoadOptions)flOptions, hostName, ref socket);
            }
            finally
            {
                this.Destroy();
                int count = 0;
                // Reconnect to the reader
                bool isLLRP = false;
                while (count <= 3)
                {
                    isLLRP = this.IsLlrpReader();
                    count++;
                    if (isLLRP)
                        break;
                    Thread.Sleep(20000);
                }
                if (!isLLRP)
                {
                    throw new ReaderException("Reader Type changed...Please reconnect");
                }
                Connect();
                isFirmwareLoadInProgress = false;
            }
        }

        #endregion

        #endregion

        #region GpiGet

        /// <summary>
        /// Get the state of all of the reader's GPI pins. 
        /// </summary>
        /// <returns>array of GpioPin objects representing the state of all input pins</returns>
        public override GpioPin[] GpiGet()
        {
            MSG_GET_READER_CONFIG msg = new MSG_GET_READER_CONFIG();
            msg.RequestedData = ENUM_GetReaderConfigRequestedData.GPIPortCurrentState;
            MSG_GET_READER_CONFIG_RESPONSE response = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msg);

            List<GpioPin> list = new List<GpioPin>();
            if (response.GPIPortCurrentState != null)
            {
                foreach (PARAM_GPIPortCurrentState state in response.GPIPortCurrentState)
                {
                    int id = LlrpToTmGpi(state.GPIPortNum);
                    bool high = (ENUM_GPIPortState.High == state.State);
                    bool output = false;
                    GpioPin pin = new GpioPin(id, high, output);
                    list.Add(pin);
                }
            }
            return list.ToArray();
        }

        int LlrpToTmGpi(ushort llrpNum)
        {
            int index = llrpNum - 1;
            if ((llrpNum < 0) || (gpiList.Length <= index))
            {
                throw new ReaderParseException(String.Format("Invalid LLRP GpiPortNum: {0}", llrpNum));
            }
            return gpiList[llrpNum - 1];
        }

        #endregion

        #region GpoSet

        /// <summary>
        /// Set the state of some GPO pins.
        /// </summary>
        /// <param name="state">array of GpioPin objects</param>
        public override void GpoSet(ICollection<GpioPin> state)
        {
            MSG_SET_READER_CONFIG msg = new MSG_SET_READER_CONFIG();
            List<PARAM_GPOWriteData> list = new List<PARAM_GPOWriteData>();
            if (null != state)
            {
                foreach (GpioPin pin in state)
                {
                    PARAM_GPOWriteData data = new PARAM_GPOWriteData();
                    data.GPOPortNumber = TmToLlrpGpo(pin.Id);
                    data.GPOData = pin.High;
                    list.Add(data);
                }
            }
            msg.GPOWriteData = list.ToArray();
            SendLlrpMessage(msg);
        }

        private Dictionary<int, ushort> _tmToLlrpGpoMap = null;
        ushort TmToLlrpGpo(int tmNum)
        {
            if (!getTmToLlrpGpoMap().ContainsKey(tmNum))
            {
                throw new ArgumentException(String.Format("Invalid GPO Number: {0}", tmNum));
            }
            return getTmToLlrpGpoMap()[tmNum];
        }
        private Dictionary<int, ushort> getTmToLlrpGpoMap()
        {
            if (null == _tmToLlrpGpoMap)
            {
                ushort llrpNum = 1;
                Dictionary<int, ushort> map = _tmToLlrpGpoMap = new Dictionary<int, ushort>();
                foreach (int tmNum in gpoList)
                {
                    map[tmNum] = llrpNum;
                    llrpNum++;
                }
            }
            return _tmToLlrpGpoMap;
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
            //Delegate to receive the Ro Access report
            llrp.OnRoAccessReportReceived += new delegateRoAccessReport(TagOpOnRoAccessReportReceived);
            reportReceived = false;
            isRoAccessReportReceived = false;
            roSpecId = 0;

            /*  Reset Reader
            * Delete all ROSpec and AccessSpecs on the reader, so that
            * we don't have to worry about the prior configuration
            * No need to verify the error status. */

            DeleteRoSpec();
            DeleteAccessSpecs();
            //Though its a standalone tag operation, From server point of view
            //we need to submit requests in the following order.

            // Add ROSpec 
            // Enable ROSpec
            // Add AccessSpec
            // Enable AccessSpec
            // Start ROSpec
            // Wait for response and verify the result

            SimpleReadPlan srp = new SimpleReadPlan();
            srp.Filter = target;
            roSpecProtocolTable = new Hashtable();
            if (tagOP.GetType().Equals(typeof(Iso180006b.ReadData))
                || tagOP.GetType().Equals(typeof(Iso180006b.WriteData))
                || tagOP.GetType().Equals(typeof(Iso180006b.LockTag)))
            {
                srp.Protocol = TagProtocol.ISO180006B;
            }
            else
            {
                srp.Protocol = (TagProtocol)ParamGet("/reader/tagop/protocol");
            }
            srp.Antennas = new int[] { (int)ParamGet("/reader/tagop/antenna") };
            srp.Op = tagOP;
            List<PARAM_ROSpec> roSpecList = new List<PARAM_ROSpec>();
            TagQueueEmptyEvent.Reset();
            try
            {
                BuildRoSpec(srp, 0, roSpecList, true);
                if (AddRoSpec(roSpecList[0]))
                {
                    if (EnableRoSpec(roSpecList[0].ROSpecID))
                    {
                        if (!StartRoSpec(roSpecList[0].ROSpecID))
                            return null;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.ToString());
            }
            WaitForSearchEnd(0);
            DateTime start = DateTime.Now;
            int timeout = (int)ParamGet("/reader/commandTimeout") + (int)ParamGet("/reader/transportTimeout");
            while (!reportReceived)
            {
                Thread.Sleep(20);
                TimeSpan timeDiff = DateTime.Now - start;
                if (timeDiff.TotalMilliseconds > timeout)
                {
                    throw new ReaderCommException("Timeout");
                }
            }
            ReaderException tempReaderexception = rx;
            try
            {
                if (null == rx)
                {
                    //Update tagOpResponse in UpdateRoReport;
                    if ((tagOP is Gen2.ReadData)
                        || (tagOP is Gen2.BlockPermaLock)
                        || (tagOP is Gen2.Impinj.Monza4.QTReadWrite)
                        || (tagOP is Gen2.NxpGen2TagOp.Calibrate)
                        || (tagOP is Gen2.NxpGen2TagOp.EasAlarm)
                        || (tagOP is Gen2.NXP.G2I.ChangeConfig)
                        || (tagOP is Iso180006b.ReadData)
                        || (tagOP is Gen2.IDS.SL900A.GetBatteryLevel)
                        || (tagOP is Gen2.IDS.SL900A.GetSensorValue)
                        || (tagOP is Gen2.IDS.SL900A.GetLogState)
                        || (tagOP is Gen2.IDS.SL900A.GetCalibrationData)
                        || (tagOP is Gen2.IDS.SL900A.GetMeasurementSetup)
                        || (tagOP is Gen2.IDS.SL900A.AccessFifoRead)
                        || (tagOP is Gen2.IDS.SL900A.AccessFifoStatus)
                        || (tagOP is Gen2.Denatran.IAV.ActivateSecureMode)
                        || (tagOP is Gen2.Denatran.IAV.ActivateSiniavMode)
                        || (tagOP is Gen2.Denatran.IAV.AuthenticateOBU)
                        || (tagOP is Gen2.Denatran.IAV.OBUAuthFullPass1)
                        || (tagOP is Gen2.Denatran.IAV.OBUAuthFullPass2)
                        || (tagOP is Gen2.Denatran.IAV.OBUAuthID)
                        || (tagOP is Gen2.Denatran.IAV.OBUReadFromMemMap)
                        || (tagOP is Gen2.Denatran.IAV.OBUWriteToMemMap)
                        || (tagOP is Gen2.NXP.AES.Authenticate)
                        || (tagOP is Gen2.NXP.AES.Untraceable)
                        || (tagOP is Gen2.NXP.AES.ReadBuffer)
                        || (tagOP is TagOpList))
                    {
                        return tagOpResponse;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    tempReaderexception = rx;
                    rx = null;
                    throw tempReaderexception;
                }
            }
            finally
            {
                tempReaderexception = null;
            }
        }

        void TagOpOnRoAccessReportReceived(MSG_RO_ACCESS_REPORT msg)
        {
            BuildTransport(false, msg);
            delegateRoAccessReport del = new delegateRoAccessReport(ParseTagOpResponse);
            del.Invoke(msg);
        }
        private void ParseTagOpResponse(MSG_RO_ACCESS_REPORT response)
        {
            TagReadData td = new TagReadData();
            UNION_AccessCommandOpSpecResult opSpecResult = response.TagReportData[0].AccessCommandOpSpecResult;
            try
            {
                if (null != opSpecResult)
                {
                    if (opSpecResult.Count > 0)
                    {
                        ParseTagOpSpecResultType(opSpecResult, ref td);
                        TagQueueEmptyEvent.Set();
                    }
                }
            }
            catch (ReaderException ex)
            {
                rx = ex;
                TagQueueEmptyEvent.Set();
            }
            finally
            {
                reportReceived = true;
            }
            llrp.OnRoAccessReportReceived -= new delegateRoAccessReport(TagOpOnRoAccessReportReceived);
        }
        #endregion ExecuteTagOp

        #region KillTag

        /// <summary>
        /// Kill a tag. The first tag seen is killed.
        /// </summary>
        /// <param name="target">the tag target</param>
        /// <param name="password">the kill password</param>
        public override void KillTag(TagFilter target, TagAuthentication password)
        {
            if (null == password)
            {
                throw new ArgumentException("KillTag requires TagAuthentication: null not allowed");
            }

            else if (password is Gen2.Password)
            {
                UInt32 pwval = ((Gen2.Password)password).Value;
                ExecuteTagOp(new Gen2.Kill(pwval), target);
            }
            else
            {
                throw new ArgumentException("Unsupported TagAuthentication: " + password.GetType().ToString());
            }
        }

        #endregion

        #region LockTag

        /// <summary>
        /// Perform a lock or unlock operation on a tag. The first tag seen
        /// is operated on - the singulation parameter may be used to control
        /// this. Note that a tag without an access password set may not
        /// accept a lock operation or remain locked.
        /// </summary>
        /// <param name="target">the tag target to operate on</param>
        /// <param name="action">the tag lock action</param>
        public override void LockTag(TagFilter target, TagLockAction action)
        {
            TagProtocol protocol = (TagProtocol)ParamGet("/reader/tagop/protocol");

            if (action is Gen2.LockAction)
            {
                if (TagProtocol.GEN2 != protocol)
                {
                    throw new ArgumentException(String.Format(
                        "Gen2.LockAction not compatible with protocol {0}",
                        protocol.ToString()));
                }

                Gen2.LockAction la = (Gen2.LockAction)action;
                Gen2.Password pwobj = (Gen2.Password)ParamGet("/reader/gen2/accessPassword");
                UInt32 accessPassword = pwobj.Value;
                ExecuteTagOp(new Gen2.Lock(accessPassword, la), target);
            }
            else if (action is Iso180006b.LockAction)
            {
                if (TagProtocol.ISO180006B != protocol)
                {
                    throw new ArgumentException(String.Format(
                        "Iso180006b.LockAction not compatible with protocol {0}",
                        protocol.ToString()));
                }

                Iso180006b.LockAction i18kaction = (Iso180006b.LockAction)action;
                ExecuteTagOp(new Iso180006b.LockTag(i18kaction.Address), target);
            }
            else
            {
                throw new ArgumentException("LockTag does not support this type of TagLockAction: " + action.ToString());
            }
        }

        #endregion

        #region WriteTag

        /// <summary>
        /// Write a new ID to a tag.
        /// </summary>
        /// <param name="target">the tag target to operate on</param>
        /// <param name="epc">the tag ID to write</param>
        public override void WriteTag(TagFilter target, TagData epc)
        {
            ExecuteTagOp(new Gen2.WriteTag(new Gen2.TagData(epc.EpcBytes)), target);
        }

        #endregion

        #region WriteTagMemBytes

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag target to operate on</param>
        /// <param name="bank">the tag memory bank</param>
        /// <param name="byteAddress">the starting memory address to write</param>
        /// <param name="data">the data to write</param>
        public override void WriteTagMemBytes(TagFilter target, int bank, int byteAddress, ICollection<byte> data)
        {
            throw new FeatureNotSupportedException("Not implemented");
        }

        #endregion

        #region WriteTagMemWords

        /// <summary>
        /// Write data to the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag target to operate on</param>
        /// <param name="bank">the tag memory bank</param>
        /// <param name="address">the memory address to write</param>
        /// <param name="data">the data to write</param>
        public override void WriteTagMemWords(TagFilter target, int bank, int address, ICollection<ushort> data)
        {
            throw new FeatureNotSupportedException("Not implemented");
        }

        #endregion

        /// <summary>
        /// Read data from the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag target to operate on</param>
        /// <param name="bank">the tag memory bank</param>
        /// <param name="address">the reading starting byte address</param>
        /// <param name="byteCount">the bytes to read</param>
        /// <returns>the bytes read</returns>
        public override byte[] ReadTagMemBytes(TagFilter target, int bank, int address, int byteCount)
        {
            throw new FeatureNotSupportedException("Not implemented");
        }

        /// <summary>
        /// Read data from the memory bank of a tag.
        /// </summary>
        /// <param name="target">the tag target to operate on</param>
        /// <param name="bank">the tag memory bank</param>
        /// <param name="wordAddress">the read starting word address</param>
        /// <param name="wordCount">the number of words to read</param>
        /// <returns>the read words</returns>
        public override ushort[] ReadTagMemWords(TagFilter target, int bank, int wordAddress, int wordCount)
        {
            throw new FeatureNotSupportedException("Not implemented");
        }

        private LTKD.Message SendLlrpMessage(LTKD.Message message)
        {
            LTKD.Message response = null;
            PARAM_LLRPStatus llrpStatus = null;
            object msgType = Enum.Parse(typeof(ENUM_LLRP_MSG_TYPE), message.MSG_TYPE.ToString(), true);
            int timeout = (int)ParamGet("/reader/commandTimeout") + (int)ParamGet("/reader/transportTimeout");

            BuildTransport(true, message);
            try
            {
                //Update msgStartTime before sending the message
                msgStartTime = DateTime.Now;
                // Store the message sent here
                msgSent = message;
                //Reset the flag before sending the message 
                isMsgRespReceived = false;
                switch ((ENUM_LLRP_MSG_TYPE)msgType)
                {
                    case ENUM_LLRP_MSG_TYPE.DELETE_ROSPEC:
                        timeout += STOP_TIMEOUT;
                        response = (MSG_DELETE_ROSPEC_RESPONSE)llrp.DELETE_ROSPEC((MSG_DELETE_ROSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_DELETE_ROSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.ADD_ROSPEC:
                        response = (MSG_ADD_ROSPEC_RESPONSE)llrp.ADD_ROSPEC((MSG_ADD_ROSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_ADD_ROSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.ENABLE_ROSPEC:
                        response = (MSG_ENABLE_ROSPEC_RESPONSE)llrp.ENABLE_ROSPEC((MSG_ENABLE_ROSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_ENABLE_ROSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.START_ROSPEC:
                        response = (MSG_START_ROSPEC_RESPONSE)llrp.START_ROSPEC((MSG_START_ROSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_START_ROSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.STOP_ROSPEC:
                        timeout += STOP_TIMEOUT;
                        response = (MSG_STOP_ROSPEC_RESPONSE)llrp.STOP_ROSPEC((MSG_STOP_ROSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_STOP_ROSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.DISABLE_ROSPEC:
                        timeout += STOP_TIMEOUT;
                        response = (MSG_DISABLE_ROSPEC_RESPONSE)llrp.DISABLE_ROSPEC((MSG_DISABLE_ROSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_DISABLE_ROSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.SET_READER_CONFIG:
                        response = (MSG_SET_READER_CONFIG_RESPONSE)llrp.SET_READER_CONFIG((MSG_SET_READER_CONFIG)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_SET_READER_CONFIG_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.GET_READER_CONFIG:
                        response = (MSG_GET_READER_CONFIG_RESPONSE)llrp.GET_READER_CONFIG((MSG_GET_READER_CONFIG)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_GET_READER_CONFIG_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.GET_READER_CAPABILITIES:
                        response = (MSG_GET_READER_CAPABILITIES_RESPONSE)llrp.GET_READER_CAPABILITIES((MSG_GET_READER_CAPABILITIES)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_GET_READER_CAPABILITIES_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.GET_ACCESSSPECS:
                        response = (MSG_GET_ACCESSSPECS_RESPONSE)llrp.GET_ACCESSSPECS((MSG_GET_ACCESSSPECS)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_GET_ACCESSSPECS_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.GET_ROSPECS:
                        response = (MSG_GET_ROSPECS_RESPONSE)llrp.GET_ROSPECS((MSG_GET_ROSPECS)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_GET_ROSPECS_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.KEEPALIVE_ACK:
                        llrp.KEEPALIVE_ACK((MSG_KEEPALIVE_ACK)message, out errorMessage, timeout);
                        break;
                    case ENUM_LLRP_MSG_TYPE.ADD_ACCESSSPEC:
                        response = (MSG_ADD_ACCESSSPEC_RESPONSE)llrp.ADD_ACCESSSPEC((MSG_ADD_ACCESSSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_ADD_ACCESSSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.ENABLE_ACCESSSPEC:
                        response = (MSG_ENABLE_ACCESSSPEC_RESPONSE)llrp.ENABLE_ACCESSSPEC((MSG_ENABLE_ACCESSSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_ENABLE_ACCESSSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.DELETE_ACCESSSPEC:
                        response = (MSG_DELETE_ACCESSSPEC_RESPONSE)llrp.DELETE_ACCESSSPEC((MSG_DELETE_ACCESSSPEC)message, out errorMessage, timeout);
                        llrpStatus = ((MSG_DELETE_ACCESSSPEC_RESPONSE)response).LLRPStatus;
                        break;
                    case ENUM_LLRP_MSG_TYPE.ENABLE_EVENTS_AND_REPORTS:
                        llrp.ENABLE_EVENTS_AND_REPORTS((MSG_ENABLE_EVENTS_AND_REPORTS)message, out errorMessage, timeout);
                        break;
                    case ENUM_LLRP_MSG_TYPE.GET_REPORT:
                        llrp.GET_REPORT((MSG_GET_REPORT)message, out errorMessage, timeout);
                        break;
                    case ENUM_LLRP_MSG_TYPE.CUSTOM_MESSAGE:
                        response = (MSG_THINGMAGIC_CONTROL_RESPONSE_POWER_CYCLE_READER)llrp.CUSTOM_MESSAGE((MSG_CUSTOM_MESSAGE)message, out errorMessage, timeout);
                        break;
                    default: throw new ReaderException("Not a valid llrp message");
                }
                if (response != null)
                {
                    //validate the received message with sent
                    isMsgRespReceived = sentReceiveMessageValidator(message, response);
                }
            }
            catch (Exception ex)
            {
                if (ErrCodeNullObjectReference == (uint)System.Runtime.InteropServices.Marshal.GetHRForException(ex))
                {
                    if (continuousReading)
                    {
                         llrp.OnGenericMessageReceived += new delegateGenericMessages(OnGenericMessageReceived);
                         while (!isMsgRespReceived)
                         {
                             String errMesg = ((ENUM_LLRP_MSG_TYPE)msgType).ToString() + " failed. Request timed out";
                             TimeoutExceptionLogicTimerTask(timeout, errMesg);
                         }
                         llrp.OnGenericMessageReceived -= new delegateGenericMessages(OnGenericMessageReceived);
                        //ReadExceptionPublisher expub = new ReadExceptionPublisher(this, new ReaderException((((ENUM_LLRP_MSG_TYPE)msgType).ToString() + " failed. Request timed out")));
                        //Thread trd = new Thread(expub.OnReadException);
                        //trd.Name = "OnReadException";
                        //trd.Start();
                    }
                    else
                    {
                        throw new ReaderException(((ENUM_LLRP_MSG_TYPE)msgType).ToString() + " failed. Request timed out");
                    }
                }
                else
                {
                    throw new Exception(ex.Message);
                }
            }
            BuildTransport(false, response);
            if (llrpStatus != null)
            {
                if (llrpStatus.StatusCode != ENUM_StatusCode.M_Success)
                {
                    throw new Exception(llrpStatus.ErrorDescription == "" ? (llrpStatus.StatusCode.ToString()) : llrpStatus.ErrorDescription);
                }
            }
            return response;
        }

        /**
         * Check high-level timeout.
         * @note msgStartTime is a global which is modified by other functions, too.
         * Any time an LLRP message is sent or received, msgStartTime is updated
         * to the current time, extending the timeout deadline.
         * @param timeout - timeout in ms
         * @param ex - timeout exception message 
         * @throws ReaderCommException
         */
        public void TimeoutExceptionLogicTimerTask(int timeout, String ex)
         {
            DateTime msgCurrentTime = DateTime.Now;
            TimeSpan diff = msgCurrentTime - msgStartTime;
            int diffTime = (int)diff.TotalMilliseconds;
            if (diffTime > timeout)
            {
                throw new ReaderCommException(ex);
            }
         }

       /**
        * Validates received message with the sent message
        * @param sentMsg - Message sent 
        * @param recMesg - Message received
        * @returns true/false.
        */
        public bool sentReceiveMessageValidator(LTKD.Message sentMsg, LTKD.Message recMesg)
        {
           object sentMsgType = Enum.Parse(typeof(ENUM_LLRP_MSG_TYPE), sentMsg.MSG_TYPE.ToString(), true);
           object recMesgType = Enum.Parse(typeof(ENUM_LLRP_MSG_TYPE), recMesg.MSG_TYPE.ToString(), true);
           if(recMesgType.ToString().Contains(sentMsgType.ToString()))
           {
              return true;
           }
           else
           {
              return false;
           }
        }

        private string SetReaderConfig()
        {
            string response = string.Empty;
            MSG_SET_READER_CONFIG msg = new MSG_SET_READER_CONFIG();
            msg.AccessReportSpec = new PARAM_AccessReportSpec();
            msg.AccessReportSpec.AccessReportTrigger = ENUM_AccessReportTriggerType.End_Of_AccessSpec;

            msg.AntennaConfiguration = new PARAM_AntennaConfiguration[1];
            msg.AntennaConfiguration[0] = new PARAM_AntennaConfiguration();
            msg.AntennaConfiguration[0].AirProtocolInventoryCommandSettings = new UNION_AirProtocolInventoryCommandSettings();

            PARAM_C1G2InventoryCommand cmd = new PARAM_C1G2InventoryCommand();
            cmd.C1G2RFControl = new PARAM_C1G2RFControl();
            cmd.C1G2RFControl.ModeIndex = 2;
            cmd.C1G2RFControl.Tari = 0;
            cmd.C1G2SingulationControl = new PARAM_C1G2SingulationControl();
            cmd.C1G2SingulationControl.Session = new LTKD.TwoBits(1);
            cmd.C1G2SingulationControl.TagPopulation = 0;
            cmd.C1G2SingulationControl.TagTransitTime = 1000;
            cmd.TagInventoryStateAware = false;

            msg.AntennaConfiguration[0].AirProtocolInventoryCommandSettings.Add(cmd);
            msg.AntennaConfiguration[0].AntennaID = 0;


            msg.AntennaConfiguration[0].RFReceiver = new PARAM_RFReceiver();
            msg.AntennaConfiguration[0].RFReceiver.ReceiverSensitivity = 12;

            msg.AntennaConfiguration[0].RFTransmitter = new PARAM_RFTransmitter();
            msg.AntennaConfiguration[0].RFTransmitter.ChannelIndex = 1;
            msg.AntennaConfiguration[0].RFTransmitter.HopTableID = 1;
            msg.AntennaConfiguration[0].RFTransmitter.TransmitPower = 61;

            msg.EventsAndReports = new PARAM_EventsAndReports();
            msg.EventsAndReports.HoldEventsAndReportsUponReconnect = false;

            msg.KeepaliveSpec = new PARAM_KeepaliveSpec();
            msg.KeepaliveSpec.KeepaliveTriggerType = ENUM_KeepaliveTriggerType.Null;
            msg.KeepaliveSpec.PeriodicTriggerValue = 0;

            msg.ReaderEventNotificationSpec = new PARAM_ReaderEventNotificationSpec();
            msg.ReaderEventNotificationSpec.EventNotificationState = new PARAM_EventNotificationState[5];
            msg.ReaderEventNotificationSpec.EventNotificationState[0] = new PARAM_EventNotificationState();
            msg.ReaderEventNotificationSpec.EventNotificationState[0].EventType = ENUM_NotificationEventType.AISpec_Event;
            msg.ReaderEventNotificationSpec.EventNotificationState[0].NotificationState = true;

            msg.ReaderEventNotificationSpec.EventNotificationState[1] = new PARAM_EventNotificationState();
            msg.ReaderEventNotificationSpec.EventNotificationState[1].EventType = ENUM_NotificationEventType.Antenna_Event;
            msg.ReaderEventNotificationSpec.EventNotificationState[1].NotificationState = true;

            msg.ReaderEventNotificationSpec.EventNotificationState[2] = new PARAM_EventNotificationState();
            msg.ReaderEventNotificationSpec.EventNotificationState[2].EventType = ENUM_NotificationEventType.GPI_Event;
            msg.ReaderEventNotificationSpec.EventNotificationState[2].NotificationState = true;

            msg.ReaderEventNotificationSpec.EventNotificationState[3] = new PARAM_EventNotificationState();
            msg.ReaderEventNotificationSpec.EventNotificationState[3].EventType = ENUM_NotificationEventType.Reader_Exception_Event;
            msg.ReaderEventNotificationSpec.EventNotificationState[3].NotificationState = true;

            msg.ReaderEventNotificationSpec.EventNotificationState[4] = new PARAM_EventNotificationState();
            msg.ReaderEventNotificationSpec.EventNotificationState[4].EventType = ENUM_NotificationEventType.RFSurvey_Event;
            msg.ReaderEventNotificationSpec.EventNotificationState[4].NotificationState = true;

            msg.ROReportSpec = new PARAM_ROReportSpec();
            msg.ROReportSpec.N = 1;
            msg.ROReportSpec.ROReportTrigger = ENUM_ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec;
            msg.ROReportSpec.TagReportContentSelector = new PARAM_TagReportContentSelector();
            msg.ROReportSpec.TagReportContentSelector.AirProtocolEPCMemorySelector = new UNION_AirProtocolEPCMemorySelector();
            PARAM_C1G2EPCMemorySelector c1g2mem = new PARAM_C1G2EPCMemorySelector();
            c1g2mem.EnableCRC = true;
            c1g2mem.EnablePCBits = true;
            msg.ROReportSpec.TagReportContentSelector.AirProtocolEPCMemorySelector.Add(c1g2mem);

            msg.ROReportSpec.TagReportContentSelector.EnableAccessSpecID = true;
            msg.ROReportSpec.TagReportContentSelector.EnableAntennaID = true;
            msg.ROReportSpec.TagReportContentSelector.EnableChannelIndex = true;
            msg.ROReportSpec.TagReportContentSelector.EnableFirstSeenTimestamp = true;
            msg.ROReportSpec.TagReportContentSelector.EnableInventoryParameterSpecID = true;
            msg.ROReportSpec.TagReportContentSelector.EnableLastSeenTimestamp = true;
            msg.ROReportSpec.TagReportContentSelector.EnablePeakRSSI = true;
            msg.ROReportSpec.TagReportContentSelector.EnableROSpecID = false;
            msg.ROReportSpec.TagReportContentSelector.EnableSpecIndex = true;
            msg.ROReportSpec.TagReportContentSelector.EnableTagSeenCount = true;

            msg.ResetToFactoryDefault = false;

            MSG_SET_READER_CONFIG_RESPONSE rsp = llrp.SET_READER_CONFIG(msg, out errorMessage, 3000);

            if (rsp != null)
            {
                response = rsp.ToString();
            }
            else if (errorMessage != null)
            {
                response = errorMessage.ToString();
            }
            else
            {
                response = "Command time out!";
            }
            return response;

        }

        private bool AddRoSpec(PARAM_ROSpec roSpec)
        {
            MSG_ADD_ROSPEC_RESPONSE response;
            try
            {
                MSG_ADD_ROSPEC roSpecMsg = new MSG_ADD_ROSPEC();
                roSpecMsg.ROSpec = roSpec;
                response = (MSG_ADD_ROSPEC_RESPONSE)SendLlrpMessage(roSpecMsg);

                if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }

        int numPlans = 0;
        uint totalWeight = 0;
        private void BuildRoSpec(ReadPlan rp, int timeOut, List<PARAM_ROSpec> roSpecList, bool isStandaloneOp)
        {
            string response = string.Empty;
            int aiSpecIterations = 1;
            List<ushort> antennaGlobal = new List<ushort>();

            // Build RO Spec based on the read plan
            if (rp is MultiReadPlan)
            {
                MultiReadPlan mrp = (MultiReadPlan)rp;
                numPlans = mrp.Plans.Length;

                // Validation to limit number of read plans to 5 if build is <5.3.2.97
                // Fixed this limitation issue in build 5.3.2.97
                if ((addInvSpecIDSupport == false) && (numPlans > maxSubPlanCnt))
                {
                    throw new Exception("Unsupported operation");
                }

                // Handle backward compatibility for Per Antenna On Time feature
                if (perAntOnTimeSupport == false)
                {
                    foreach (ReadPlan r in mrp.Plans)
                    {
                        // Ideally, totalWeight=0 would allow reader to
                        // dynamically adjust timing based on tags observed.
                        // For now, just divide equally.
                        int subtimeout =
                            (mrp.TotalWeight != 0) ? ((int)timeOut * r.Weight / mrp.TotalWeight)
                            : (timeOut / mrp.Plans.Length);
                        totalWeight = (uint)mrp.TotalWeight;
                        subtimeout = Math.Min(subtimeout, UInt16.MaxValue);
                        BuildRoSpec(r, subtimeout, roSpecList, false);
                    }
                    return;
                }
            }

            // Create a Reader Operation Spec (ROSpec).
            PARAM_ROSpec roSpec = new PARAM_ROSpec();
            roSpec.CurrentState = ENUM_ROSpecState.Disabled;
            roSpec.Priority = 0;
            roSpec.ROSpecID = ++roSpecId;

            // Set up the ROBoundarySpec
            // This defines the start and stop triggers.
            roSpec.ROBoundarySpec = new PARAM_ROBoundarySpec();

            // ROSpec start trigger is set to null in all cases.
            // Because sending complete multi read plan information in multiple AISpecs in single ROSPec
            // No need to periodically execute ROSpec incase of multi read async
            roSpec.ROBoundarySpec.ROSpecStartTrigger = new PARAM_ROSpecStartTrigger();

            if (perAntOnTimeSupport == true)
            {
                // Set start trigger to BoundarySpec
                roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType = ENUM_ROSpecStartTriggerType.Null;
            }
            else
            {
                // For backward compatibility case - No per antenna on time feature
                uint asyncOnTime = Convert.ToUInt16(ParamGet("/reader/read/asyncOnTime"));
                if (continuousReading && numPlans > 1)
                {
                    roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType = ENUM_ROSpecStartTriggerType.Periodic;
                    PARAM_PeriodicTriggerValue pValue = new PARAM_PeriodicTriggerValue();
                    pValue.Offset = 0;
                    pValue.Period = asyncOnTime;
                    roSpec.ROBoundarySpec.ROSpecStartTrigger.PeriodicTriggerValue = pValue;
                }
                else
                {
                    if (!continuousReading && (rp is StopTriggerReadPlan))
                    {
                        // Currently only supported for sync read case
                        StopTriggerReadPlan strp = (StopTriggerReadPlan)rp;
                        if (strp.stopOnCount is StopOnTagCount)
                        {
                            isStopNTags = true;
                            StopOnTagCount sotc = (StopOnTagCount)strp.stopOnCount;
                            numberOfTagsToRead = sotc.N;
                            if (isStopNTags && (numberOfTagsToRead == 0))
                            {
                                throw new ArgumentException("Invalid number of tag count found");
                            }
                        }
                    }
                    roSpec.ROBoundarySpec.ROSpecStartTrigger.ROSpecStartTriggerType = ENUM_ROSpecStartTriggerType.Null;
                }
            }

            // Set stop triggers to BoundarySpec
            roSpec.ROBoundarySpec.ROSpecStopTrigger = new PARAM_ROSpecStopTrigger();
            if (perAntOnTimeSupport == true)
            {
                if (!continuousReading && numPlans > 1)
                {
                    // In case of non continuous reading and if multiple read plans exist,
                    // then ROSpec's stop trigger is set to
                    // duration trigger with the duration set to total read duration.
                    roSpec.ROBoundarySpec.ROSpecStopTrigger.ROSpecStopTriggerType = ENUM_ROSpecStopTriggerType.Duration;
                    roSpec.ROBoundarySpec.ROSpecStopTrigger.DurationTriggerValue = (uint)timeOut;
                }
                else
                {
                    // ROSpec stop trigger is set to null in all cases.
                    roSpec.ROBoundarySpec.ROSpecStopTrigger.ROSpecStopTriggerType = ENUM_ROSpecStopTriggerType.Null;
                    roSpec.ROBoundarySpec.ROSpecStopTrigger.DurationTriggerValue = 0;
                }
            }
            else
            {
                roSpec.ROBoundarySpec.ROSpecStopTrigger.ROSpecStopTriggerType = ENUM_ROSpecStopTriggerType.Null;
                roSpec.ROBoundarySpec.ROSpecStopTrigger.DurationTriggerValue = 0;
            }

            if (perAntOnTimeSupport == true && numPlans > 1)
            {
                // Add an Antenna Inventory Spec (AISpec)
                // If Multi readplan is set, AISpecIterations should be equal to number of simple read plans.
                aiSpecIterations = numPlans;
            }
            for (int planCount = 0; planCount < aiSpecIterations; planCount++)
            {
                PARAM_AISpec aiSpec = new PARAM_AISpec();
                PARAM_AISpecStopTrigger aiStopTrigger = new PARAM_AISpecStopTrigger();
                if (perAntOnTimeSupport == true)
                {
                    if ((!continuousReading) && (aiSpecIterations == 1))
                    {
                        // In sync read and AI spec stop
                        // trigger should be duration based.
                        // Currently only supported for sync read case
                        if (rp is StopTriggerReadPlan)
                        {
                            StopTriggerReadPlan strp = (StopTriggerReadPlan)rp;
                            if (strp.stopOnCount is StopOnTagCount)
                            {
                                isStopNTags = true;
                                StopOnTagCount sotc = (StopOnTagCount)strp.stopOnCount;
                                numberOfTagsToRead = sotc.N;
                                if (isStopNTags && (numberOfTagsToRead == 0))
                                {
                                    throw new ArgumentException("Invalid number of tag count found");
                                }
                            }
                        }
                        if (isStopNTags)
                        {
                            PARAM_TagObservationTrigger tagObsTrigger = new PARAM_TagObservationTrigger();
                            // setting trigger type as Upon_Seeing_N_Tags_Or_Timeout (corresponding enum value is 0)
                            tagObsTrigger.NumberOfTags = (ushort)numberOfTagsToRead;
                            tagObsTrigger.Timeout = (UInt32)timeOut;
                            tagObsTrigger.TriggerType = ENUM_TagObservationTriggerType.Upon_Seeing_N_Tags_Or_Timeout;
                            tagObsTrigger.T = 0;
                            tagObsTrigger.NumberOfAttempts = 0;

                            aiStopTrigger.TagObservationTrigger = tagObsTrigger;
                            aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Tag_Observation;
                            aiStopTrigger.DurationTrigger = 0;
                        }
                        else
                        {
                            // SYNC Mode - Set the AI stop trigger to inputted duration. AI spec will run for particular duration
                            aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Duration;
                            aiStopTrigger.DurationTrigger = (uint)timeOut;
                        }
                    }
                    else
                    {
                        // In all other cases, i.e., for both sync and async read
                        // AISpec stop trigger should be NULL.
                        aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Null;
                        aiStopTrigger.DurationTrigger = 0;
                    }
                }
                else
                {
                    if (continuousReading)
                    {
                        // ASYNC Mode - Set the AI stop trigger to null. AI spec will run until the ROSpec stops.
                        if (numPlans > 1)
                        {
                            // ASYNC Mode - Set the AI stop trigger to Duration - AsyncOnTime. AI spec will run until the Disable ROSpec is sent.
                            aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Duration;
                            aiStopTrigger.DurationTrigger = (uint)timeOut;
                        }
                        else
                        {
                            aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Null;
                            aiStopTrigger.DurationTrigger = 0;
                        }
                    }
                    else
                    {
                        // In all other cases, i.e., for both sync and async read AISpec
                        // stop trigger should be duration based.
                        if (isStopNTags)
                        {
                            PARAM_TagObservationTrigger tagObsTrigger = new PARAM_TagObservationTrigger();
                            tagObsTrigger.NumberOfTags = (ushort)numberOfTagsToRead;
                            tagObsTrigger.T = 0;
                            tagObsTrigger.NumberOfAttempts = 0;
                            tagObsTrigger.Timeout = (UInt32)timeOut;
                            tagObsTrigger.TriggerType = ENUM_TagObservationTriggerType.Upon_Seeing_N_Tags_Or_Timeout;

                            aiStopTrigger.TagObservationTrigger = tagObsTrigger;
                            aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Tag_Observation;
                        }
                        else
                        {
                            // SYNC Mode - Set the AI stop trigger to inputted duration. AI spec will run for particular duration
                            aiStopTrigger.AISpecStopTriggerType = ENUM_AISpecStopTriggerType.Duration;
                            aiStopTrigger.DurationTrigger = (uint)timeOut;
                        }
                    }
                }

                // Set AISpec stop trigger
                aiSpec.AISpecStopTrigger = aiStopTrigger;

                // Select which antenna ports we want to use based on the read plan settings, and set antenna ids to aispec
                bool isFastSearch = false;
                bool perAntFastSearch = false;
                int[] antennaList = new int[] { };
                TagFilter tagFilter = null;

                TagProtocol protocol = TagProtocol.NONE;
                TagOp tagOperation = null;
                CustomAntConfig customAntConfig = null;
                if (rp is SimpleReadPlan)
                {
                    SimpleReadPlan srp = (SimpleReadPlan)rp;
                    isFastSearch = srp.UseFastSearch;
                    antennaList = srp.Antennas;
                    ValidateProtocol(srp.Protocol);
                    protocol = srp.Protocol;
                    if (!isStandaloneOp)
                    {
                        //Add rospec id and protocol in the hashtable so that it can be used to
                        //populate the tagdata's protocol member with the read tag protocol.
                        roSpecProtocolTable.Add(roSpec.ROSpecID, srp.Protocol);
                    }
                    tagOperation = srp.Op;

                    if (srp.CustAntConfig == null)
                        tagFilter = srp.Filter;
                    else
                    {
                        customAntConfig = srp.CustAntConfig;
                        perAntFastSearch = customAntConfig.perAntFastSearch;
                    }
                }
                else if (rp is MultiReadPlan)
                {
                    MultiReadPlan mrp = (MultiReadPlan)rp;
                    ReadPlan[] rplans = mrp.Plans;

                    if (rplans[planCount] is SimpleReadPlan)
                    {
                        SimpleReadPlan srp = (SimpleReadPlan)rplans[planCount];
                        isFastSearch = srp.UseFastSearch;
                        antennaList = srp.Antennas;
                        ValidateProtocol(srp.Protocol);
                        protocol = srp.Protocol;
                        tagOperation = srp.Op;
                        if (srp.CustAntConfig == null)
                            tagFilter = srp.Filter;
                        else
                        {
                            customAntConfig = srp.CustAntConfig;
                            perAntFastSearch = customAntConfig.perAntFastSearch;
                        }
                    }
                }

                // Select which antenna ports we want to use based on the read plan settings
                // Setting this property to 0 means all antenna ports
                aiSpec.AntennaIDs = new LTKD.UInt16Array();

                if (null == antennaList)
                {
                    aiSpec.AntennaIDs.Add(0);               //0 :  applies to all antenna, 
                    antennaGlobal.Add(0);
                }
                else
                {
                    foreach (int antenna in antennaList)
                    {
                        aiSpec.AntennaIDs.Add((ushort)antenna);
                        antennaGlobal.Add((ushort)antenna);
                    }
                }

                // Reading Gen2 Tags, specify in InventorySpec
                PARAM_InventoryParameterSpec inventoryParam = new PARAM_InventoryParameterSpec();
                List<PARAM_InventoryParameterSpec> invParamList = new List<PARAM_InventoryParameterSpec>();
                inventoryParam.InventoryParameterSpecID = 1;
                List<PARAM_AntennaConfiguration> antennaConfigList = new List<PARAM_AntennaConfiguration>();
                if (customAntConfig != null)
                {
                    //setting per antenna configurations based on the no of antennas provided
                    //a loop will run to set parameters per antenna 
                    for (int cntAnt = 0; cntAnt < customAntConfig.customConfigPerAnt.Count; cntAnt++)
                    {
                        PARAM_AntennaConfiguration antConfig = new PARAM_AntennaConfiguration();
                        antConfig.AntennaID = (ushort)(customAntConfig.customConfigPerAnt[cntAnt].antID);
                        if (TagProtocol.GEN2.Equals(protocol))
                        {
                            List<PARAM_C1G2Filter> filterList = new List<PARAM_C1G2Filter>();
                            PARAM_C1G2Filter filter = new PARAM_C1G2Filter();
                            PARAM_C1G2TagInventoryMask mask;
                            filter.T = ENUM_C1G2TruncateAction.Do_Not_Truncate;

                            PARAM_C1G2InventoryCommand inventoryCommand = new PARAM_C1G2InventoryCommand();
                            if (customAntConfig.customConfigPerAnt[cntAnt].filter is Gen2.Select)
                            {
                                if (multiselectSupport == true)
                                {
                                    inventoryCommand.TagInventoryStateAware = true;
                                }
                                else
                                {
                                    inventoryCommand.TagInventoryStateAware = false;
                                }

                                Gen2.Select selectFilter = (Gen2.Select)customAntConfig.customConfigPerAnt[cntAnt].filter;
                                mask = new PARAM_C1G2TagInventoryMask();

                                // Memory Bank
                                if (((ushort)selectFilter.Bank) > 3)
                                {
                                    throw new Exception("Invalid argument");
                                }
                                mask.MB = new LTKD.TwoBits((ushort)selectFilter.Bank);
                                // Validate bitLength and mask.length. Always ensure, bitlength should be less than or equal to mask.length
                                if (selectFilter.BitLength > (selectFilter.Mask.Length * 8))
                                {
                                    throw new Exception("Bit Length cannot be greater than mask length");
                                }
                                else
                                {
                                    // LLRP Spec doesn't support filter operation, if bitLength is not a multiple of 8.
                                    if ((selectFilter.BitLength % 8) != 0)
                                    {
                                        throw new Exception("Can't parse bitLength " + selectFilter.BitLength + " in multiples of 8."
                                            + "Please provide bitLength in multiples of 8.");
                                    }
                                    int length = selectFilter.BitLength / 8;
                                    byte[] tempMask = new byte[length];
                                    Array.Copy(selectFilter.Mask, 0, tempMask, 0, length);
                                    mask.TagMask = LTKD.LLRPBitArray.FromHexString(ByteFormat.ToHex(tempMask).Split('x')[1].ToString());
                                }

                                mask.Pointer = (ushort)selectFilter.BitPointer;
                                filter.C1G2TagInventoryMask = mask;

                                if (multiselectSupport == true)
                                {
                                    // Set TagInventory StateAware Action
                                    PARAM_C1G2TagInventoryStateAwareFilterAction awareAction = new PARAM_C1G2TagInventoryStateAwareFilterAction();
                                    if (isStateAwareTargetMapped)
                                    {
                                        switch ((ENUM_C1G2StateAwareTarget)selectFilter.target)
                                        {
                                            case (ENUM_C1G2StateAwareTarget)0:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S0;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)1:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S1;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)2:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S2;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)3:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S3;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)4:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.SL;
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        awareAction.Target = (ENUM_C1G2StateAwareTarget)selectFilter.target;
                                    }
                                    awareAction.Action = (ENUM_C1G2StateAwareAction)selectFilter.action;
                                    filter.C1G2TagInventoryStateAwareFilterAction = awareAction;
                                }
                                else
                                {
                                    PARAM_C1G2TagInventoryStateUnawareFilterAction unAwareAction = new PARAM_C1G2TagInventoryStateUnawareFilterAction();
                                    unAwareAction.Action = ENUM_C1G2StateUnawareAction.Select_Unselect;

                                    if (selectFilter.Invert)
                                    {
                                        unAwareAction.Action = ENUM_C1G2StateUnawareAction.Unselect_Select;
                                    }
                                    filter.C1G2TagInventoryStateUnawareFilterAction = unAwareAction;
                                }
                            }
                            else if (customAntConfig.customConfigPerAnt[cntAnt].filter is TagData)
                            {
                                // Set TagInventoryStateAwareAction to false
                                inventoryCommand.TagInventoryStateAware = false;
                                TagData tagDataFilter = (TagData)customAntConfig.customConfigPerAnt[cntAnt].filter;
                                mask = new PARAM_C1G2TagInventoryMask();
                                // EPC Memory Bank 
                                mask.MB = new LTKD.TwoBits((ushort)Gen2.Bank.EPC);
                                mask.TagMask = LTKD.LLRPBitArray.FromHexString(tagDataFilter.EpcString);
                                //For epc bit pointer is 32
                                mask.Pointer = 32;
                                filter.C1G2TagInventoryMask = mask;
                                PARAM_C1G2TagInventoryStateUnawareFilterAction unAwareAction = new PARAM_C1G2TagInventoryStateUnawareFilterAction();
                                unAwareAction.Action = ENUM_C1G2StateUnawareAction.Select_Unselect;
                                filter.C1G2TagInventoryStateUnawareFilterAction = unAwareAction;
                            }
                            else
                            {
                                throw new Exception("Invalid select type");
                            }
                            filterList.Add(filter);
                            inventoryCommand.C1G2Filter = filterList.ToArray();
                            antConfig.AirProtocolInventoryCommandSettings.Add(inventoryCommand);
                            antennaConfigList.Add(antConfig);

                            PARAM_C1G2SingulationControl singulation = new PARAM_C1G2SingulationControl();
                            singulation.Session = new LTKD.TwoBits(Convert.ToUInt16(customAntConfig.customConfigPerAnt[cntAnt].session));
                            inventoryCommand.C1G2SingulationControl = singulation;

                            PARAM_ThingMagicTargetStrategy target = new PARAM_ThingMagicTargetStrategy();
                            target.ThingMagicTargetStrategyValue = (ENUM_ThingMagicC1G2TargetStrategy)customAntConfig.customConfigPerAnt[cntAnt].target;
                            inventoryCommand.Custom.Add(target);

                            if (perAntFastSearch)
                            {
                                PARAM_ThingMagicFastSearchMode fastSearch = new PARAM_ThingMagicFastSearchMode();
                                fastSearch.ThingMagicFastSearch = ENUM_ThingMagicFastSearchValue.Enabled;
                                inventoryCommand.AddCustomParameter(fastSearch);
                            }
                            inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                        } //end of Gen2 filter
                        else if (TagProtocol.ISO180006B.Equals(protocol))
                        {
                            if (tagFilter is Iso180006b.Select)
                            {
                                PARAM_ThingMagicISO180006BTagPattern tagPattern = new PARAM_ThingMagicISO180006BTagPattern();
                                //Filter type
                                tagPattern.FilterType = ENUM_ThingMagicISO180006BFilterType.ISO180006BSelect;
                                //Invert
                                tagPattern.Invert = ((Iso180006b.Select)tagFilter).Invert;
                                //Address
                                tagPattern.Address = Convert.ToByte(((Iso180006b.Select)tagFilter).Address.ToString("X"));
                                //Mask
                                tagPattern.Mask = ((Iso180006b.Select)tagFilter).Mask;
                                //SelectOp
                                if ((Convert.ToUInt16(((Iso180006b.Select)tagFilter).Op)) > 3)
                                {
                                    throw new Exception("Invalid argument");
                                }
                                tagPattern.SelectOp = new Org.LLRP.LTK.LLRPV1.DataType.TwoBits(Convert.ToUInt16(((Iso180006b.Select)tagFilter).Op));
                                //TagData
                                tagPattern.TagData = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(((Iso180006b.Select)tagFilter).Data).Split('x')[1]);
                                PARAM_ThingMagicISO180006BInventoryCommand iso18k6bInventoryCmd = new PARAM_ThingMagicISO180006BInventoryCommand();
                                iso18k6bInventoryCmd.ThingMagicISO180006BTagPattern = tagPattern;
                                antConfig.AirProtocolInventoryCommandSettings.Add(iso18k6bInventoryCmd);
                                antennaConfigList.Add(antConfig);
                                inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                            }
                            else if (tagFilter is TagData)
                            {
                                PARAM_ThingMagicISO180006BTagPattern tagPattern = new PARAM_ThingMagicISO180006BTagPattern();
                                //Filter type
                                tagPattern.FilterType = ENUM_ThingMagicISO180006BFilterType.ISO180006BTagData;
                                // Mercury API doesn't expect invert flag and SelectOp field  from TagData filter
                                // so defaulting 0
                                //Invert
                                tagPattern.Invert = false;
                                //Address
                                tagPattern.Address = 0;
                                //Mask
                                tagPattern.Mask = 0xff;
                                //SelectOp
                                tagPattern.SelectOp = new Org.LLRP.LTK.LLRPV1.DataType.TwoBits(Convert.ToUInt16(((Iso180006b.SelectOp.EQUALS))));
                                //TagData
                                tagPattern.TagData = LTKD.ByteArray.FromHexString(((TagData)tagFilter).EpcString);
                                PARAM_ThingMagicISO180006BInventoryCommand iso18k6bInventoryCmd = new PARAM_ThingMagicISO180006BInventoryCommand();
                                iso18k6bInventoryCmd.ThingMagicISO180006BTagPattern = tagPattern;
                                antConfig.AirProtocolInventoryCommandSettings.Add(iso18k6bInventoryCmd);
                                antennaConfigList.Add(antConfig);
                            }
                            else
                            {
                                throw new Exception("Invalid select type");
                            }
                        }//end of ISO180006b filter
                        else
                        {
                            throw new FeatureNotSupportedException("Only GEN2 and ISO18K6B protocol is supported as of now");
                        }
                    }
                }
                else if (tagFilter != null)
                {
                    PARAM_AntennaConfiguration antConfig = new PARAM_AntennaConfiguration();
                    antConfig.AntennaID = 0;
                    if (TagProtocol.GEN2.Equals(protocol))
                    {
                        if ((tagFilter is MultiFilter) && (multiselectSupport == true))
                        {
                            MultiFilter multiFilter = (MultiFilter)tagFilter;
                            TagFilter[] filters = multiFilter.filters;

                            PARAM_C1G2InventoryCommand inventoryCommand = new PARAM_C1G2InventoryCommand();
                            inventoryCommand.TagInventoryStateAware = true;
                            List<PARAM_C1G2Filter> filterList = new List<PARAM_C1G2Filter>();

                            for (int i = 0; i < filters.Length; i++)
                            {
                                PARAM_C1G2Filter filter = new PARAM_C1G2Filter();
                                PARAM_C1G2TagInventoryMask mask;
                                filter.T = ENUM_C1G2TruncateAction.Do_Not_Truncate;

                                // If Gen2 protocol select filter
                                if (filters[i] is Gen2.Select)
                                {
                                    inventoryCommand.TagInventoryStateAware = true;

                                    Gen2.Select selectFilter = (Gen2.Select)filters[i];
                                    mask = new PARAM_C1G2TagInventoryMask();

                                    // Memory Bank
                                    if (((ushort)selectFilter.Bank) > 3)
                                    {
                                        throw new Exception("Invalid argument");
                                    }
                                    mask.MB = new LTKD.TwoBits((ushort)selectFilter.Bank);
                                    // Validate bitLength and mask.length. Always ensure, bitlength should be less than or equal to mask.length
                                    if (selectFilter.BitLength > (selectFilter.Mask.Length * 8))
                                    {
                                        throw new Exception("Bit Length cannot be greater than mask length");
                                    }
                                    else
                                    {
                                        // LLRP Spec doesn't support filter operation, if bitLength is not a multiple of 8.
                                        if ((selectFilter.BitLength % 8) != 0)
                                        {
                                            throw new Exception("Can't parse bitLength " + selectFilter.BitLength + " in multiples of 8."
                                                + "Please provide bitLength in multiples of 8.");
                                        }
                                        int length = selectFilter.BitLength / 8;
                                        byte[] tempMask = new byte[length];
                                        Array.Copy(selectFilter.Mask, 0, tempMask, 0, length);
                                        mask.TagMask = LTKD.LLRPBitArray.FromHexString(ByteFormat.ToHex(tempMask).Split('x')[1].ToString());
                                    }

                                    mask.Pointer = (ushort)selectFilter.BitPointer;
                                    filter.C1G2TagInventoryMask = mask;

                                    PARAM_C1G2TagInventoryStateAwareFilterAction awareAction = new PARAM_C1G2TagInventoryStateAwareFilterAction();
                                    if (isStateAwareTargetMapped)
                                    {
                                        switch ((ENUM_C1G2StateAwareTarget)selectFilter.target)
                                        {
                                            case (ENUM_C1G2StateAwareTarget)0:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S0;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)1:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S1;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)2:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S2;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)3:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S3;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)4:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.SL;
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        awareAction.Target = (ENUM_C1G2StateAwareTarget)selectFilter.target;
                                    }

                                    awareAction.Action = (ENUM_C1G2StateAwareAction)selectFilter.action;
                                    filter.C1G2TagInventoryStateAwareFilterAction = awareAction;
                                }
                                else
                                {
                                    throw new Exception("Unsupported operation");
                                }
                                filterList.Add(filter);
                                inventoryCommand.C1G2Filter = filterList.ToArray();
                            }
                            antConfig.AirProtocolInventoryCommandSettings.Add(inventoryCommand);
                            antennaConfigList.Add(antConfig);
                            inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                        }
                        else if ((tagFilter is MultiFilter) && (multiselectSupport == false))
                        {
                            throw new Exception("Unsupported operation");
                        }
                        else
                        {
                            List<PARAM_C1G2Filter> filterList = new List<PARAM_C1G2Filter>();
                            PARAM_C1G2Filter filter = new PARAM_C1G2Filter();
                            PARAM_C1G2TagInventoryMask mask;
                            filter.T = ENUM_C1G2TruncateAction.Do_Not_Truncate;

                            PARAM_C1G2InventoryCommand inventoryCommand = new PARAM_C1G2InventoryCommand();

                            if (tagFilter is Gen2.Select)
                            {
                                if (multiselectSupport == true)
                                {
                                    inventoryCommand.TagInventoryStateAware = true;
                                }
                                else
                                {
                                    inventoryCommand.TagInventoryStateAware = false;
                                }

                                Gen2.Select selectFilter = (Gen2.Select)tagFilter;
                                mask = new PARAM_C1G2TagInventoryMask();

                                // Memory Bank
                                if (((ushort)selectFilter.Bank) > 3)
                                {
                                    throw new Exception("Invalid argument");
                                }
                                mask.MB = new LTKD.TwoBits((ushort)selectFilter.Bank);
                                // Validate bitLength and mask.length. Always ensure, bitlength should be less than or equal to mask.length
                                if (selectFilter.BitLength > (selectFilter.Mask.Length * 8))
                                {
                                    throw new Exception("Bit Length cannot be greater than mask length");
                                }
                                else
                                {
                                    // LLRP Spec doesn't support filter operation, if bitLength is not a multiple of 8.
                                    if ((selectFilter.BitLength % 8) != 0)
                                    {
                                        throw new Exception("Can't parse bitLength " + selectFilter.BitLength + " in multiples of 8."
                                            + "Please provide bitLength in multiples of 8.");
                                    }
                                    int length = selectFilter.BitLength / 8;
                                    byte[] tempMask = new byte[length];
                                    Array.Copy(selectFilter.Mask, 0, tempMask, 0, length);
                                    mask.TagMask = LTKD.LLRPBitArray.FromHexString(ByteFormat.ToHex(tempMask).Split('x')[1].ToString());
                                }

                                mask.Pointer = (ushort)selectFilter.BitPointer;
                                filter.C1G2TagInventoryMask = mask;

                                if (multiselectSupport == true)
                                {
                                    // Set TagInventory StateAware Action
                                    PARAM_C1G2TagInventoryStateAwareFilterAction awareAction = new PARAM_C1G2TagInventoryStateAwareFilterAction();
                                    if (isStateAwareTargetMapped)
                                    {
                                        switch ((ENUM_C1G2StateAwareTarget)selectFilter.target)
                                        {
                                            case (ENUM_C1G2StateAwareTarget)0:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S0;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)1:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S1;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)2:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S2;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)3:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.Inventoried_State_For_Session_S3;
                                                break;
                                            case (ENUM_C1G2StateAwareTarget)4:
                                                awareAction.Target = ENUM_C1G2StateAwareTarget.SL;
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        awareAction.Target = (ENUM_C1G2StateAwareTarget)selectFilter.target;
                                    }

                                    awareAction.Action = (ENUM_C1G2StateAwareAction)selectFilter.action;
                                    filter.C1G2TagInventoryStateAwareFilterAction = awareAction;
                                }
                                else
                                {
                                    PARAM_C1G2TagInventoryStateUnawareFilterAction unAwareAction = new PARAM_C1G2TagInventoryStateUnawareFilterAction();
                                    unAwareAction.Action = ENUM_C1G2StateUnawareAction.Select_Unselect;

                                    if (selectFilter.Invert)
                                    {
                                        unAwareAction.Action = ENUM_C1G2StateUnawareAction.Unselect_Select;
                                    }
                                    filter.C1G2TagInventoryStateUnawareFilterAction = unAwareAction;
                                }
                            }
                            else if (tagFilter is TagData)
                            {
                                // Set TagInventoryStateAwareAction to false
                                inventoryCommand.TagInventoryStateAware = false;

                                TagData tagDataFilter = (TagData)tagFilter;
                                mask = new PARAM_C1G2TagInventoryMask();

                                // EPC Memory Bank 
                                mask.MB = new LTKD.TwoBits((ushort)Gen2.Bank.EPC);
                                mask.TagMask = LTKD.LLRPBitArray.FromHexString(tagDataFilter.EpcString);
                                //For epc bit pointer is 32
                                mask.Pointer = 32;

                                filter.C1G2TagInventoryMask = mask;

                                PARAM_C1G2TagInventoryStateUnawareFilterAction unAwareAction = new PARAM_C1G2TagInventoryStateUnawareFilterAction();
                                unAwareAction.Action = ENUM_C1G2StateUnawareAction.Select_Unselect;
                                filter.C1G2TagInventoryStateUnawareFilterAction = unAwareAction;
                            }
                            else
                            {
                                throw new Exception("Invalid select type");
                            }
                            filterList.Add(filter);
                            inventoryCommand.C1G2Filter = filterList.ToArray();
                            antConfig.AirProtocolInventoryCommandSettings.Add(inventoryCommand);
                            antennaConfigList.Add(antConfig);
                            inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                        }
                    } //end of Gen2 filter
                    else if (TagProtocol.ISO180006B.Equals(protocol))
                    {
                        if (tagFilter is Iso180006b.Select)
                        {
                            PARAM_ThingMagicISO180006BTagPattern tagPattern = new PARAM_ThingMagicISO180006BTagPattern();
                            //Filter type
                            tagPattern.FilterType = ENUM_ThingMagicISO180006BFilterType.ISO180006BSelect;
                            //Invert
                            tagPattern.Invert = ((Iso180006b.Select)tagFilter).Invert;
                            //Address
                            tagPattern.Address = Convert.ToByte(((Iso180006b.Select)tagFilter).Address.ToString("X"));
                            //Mask
                            tagPattern.Mask = ((Iso180006b.Select)tagFilter).Mask;
                            //SelectOp
                            if ((Convert.ToUInt16(((Iso180006b.Select)tagFilter).Op)) > 3)
                            {
                                throw new Exception("Invalid argument");
                            }
                            tagPattern.SelectOp = new Org.LLRP.LTK.LLRPV1.DataType.TwoBits(Convert.ToUInt16(((Iso180006b.Select)tagFilter).Op));
                            //TagData
                            tagPattern.TagData = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(((Iso180006b.Select)tagFilter).Data).Split('x')[1]);
                            PARAM_ThingMagicISO180006BInventoryCommand iso18k6bInventoryCmd = new PARAM_ThingMagicISO180006BInventoryCommand();
                            iso18k6bInventoryCmd.ThingMagicISO180006BTagPattern = tagPattern;
                            antConfig.AirProtocolInventoryCommandSettings.Add(iso18k6bInventoryCmd);
                            antennaConfigList.Add(antConfig);
                            inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                        }
                        else if (tagFilter is TagData)
                        {
                            PARAM_ThingMagicISO180006BTagPattern tagPattern = new PARAM_ThingMagicISO180006BTagPattern();
                            //Filter type
                            tagPattern.FilterType = ENUM_ThingMagicISO180006BFilterType.ISO180006BTagData;
                            // Mercury API doesn't expect invert flag and SelectOp field  from TagData filter
                            // so defaulting 0
                            //Invert
                            tagPattern.Invert = false;
                            //Address
                            tagPattern.Address = 0;
                            //Mask
                            tagPattern.Mask = 0xff;
                            //SelectOp
                            tagPattern.SelectOp = new Org.LLRP.LTK.LLRPV1.DataType.TwoBits(Convert.ToUInt16(((Iso180006b.SelectOp.EQUALS))));
                            //TagData
                            tagPattern.TagData = LTKD.ByteArray.FromHexString(((TagData)tagFilter).EpcString);
                            PARAM_ThingMagicISO180006BInventoryCommand iso18k6bInventoryCmd = new PARAM_ThingMagicISO180006BInventoryCommand();
                            iso18k6bInventoryCmd.ThingMagicISO180006BTagPattern = tagPattern;
                            antConfig.AirProtocolInventoryCommandSettings.Add(iso18k6bInventoryCmd);
                            antennaConfigList.Add(antConfig);
                            inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                        }
                        else
                        {
                            throw new Exception("Invalid select type");
                        }
                    }//end of ISO180006b filter
                    else
                    {
                        throw new FeatureNotSupportedException("Only GEN2 and ISO18K6B protocol is supported as of now");
                    }
                }//end of tagfilter

                if (isFastSearch)
                {
                    PARAM_AntennaConfiguration antConfig = new PARAM_AntennaConfiguration();
                    PARAM_ThingMagicFastSearchMode fastSearch = new PARAM_ThingMagicFastSearchMode();
                    fastSearch.ThingMagicFastSearch = ENUM_ThingMagicFastSearchValue.Enabled;
                    PARAM_C1G2InventoryCommand inventoryCommandFastSearch = new PARAM_C1G2InventoryCommand();
                    inventoryCommandFastSearch.AddCustomParameter(fastSearch);
                    inventoryCommandFastSearch.TagInventoryStateAware = false;
                    antConfig.AirProtocolInventoryCommandSettings.Add(inventoryCommandFastSearch);
                    antennaConfigList.Add(antConfig);
                    inventoryParam.AntennaConfiguration = antennaConfigList.ToArray();
                }

                invSpecId = (uint)(planCount + 1);
                PARAM_AccessSpec accessSpec = new PARAM_AccessSpec();
                if (null != tagOperation)
                {
                    // If multi read plan
                    //if(numPlans > 1)
                    if (rp is MultiReadPlan)
                    {
                        MultiReadPlan mrp = (MultiReadPlan)rp;
                        ReadPlan[] rplans = mrp.Plans;
                        if (rplans[planCount] is SimpleReadPlan)
                        {
                            SimpleReadPlan srp = (SimpleReadPlan)rplans[planCount];
                            accessSpec = createAccessSpec(srp, isStandaloneOp);
                            AddAccessSpec(accessSpec);
                            EnableAccessSpec(accessSpec.AccessSpecID);
                        }
                    }
                    else
                    {
                        // If simple read plan
                        if (rp is SimpleReadPlan)
                        {
                            SimpleReadPlan srp = (SimpleReadPlan)rp;
                            accessSpec = createAccessSpec(srp, isStandaloneOp);
                            AddAccessSpec(accessSpec);
                            EnableAccessSpec(accessSpec.AccessSpecID);
                        }
                    }
                }
                // Reading Gen2 Tags, specify in InventorySpec
                if (TagProtocol.GEN2 == protocol)
                {
                    inventoryParam.ProtocolID = ENUM_AirProtocols.EPCGlobalClass1Gen2;
                }
                else if (TagProtocol.ISO180006B == protocol)
                {
                    inventoryParam.ProtocolID = ENUM_AirProtocols.Unspecified;
                    PARAM_ThingMagicCustomAirProtocols airProtocol = new PARAM_ThingMagicCustomAirProtocols();
                    airProtocol.customProtocolId = ENUM_ThingMagicCustomAirProtocolList.Iso180006b;
                    inventoryParam.Custom.Add(airProtocol);
                }
                else if (TagProtocol.IPX64 == protocol)
                {
                    inventoryParam.ProtocolID = ENUM_AirProtocols.Unspecified;
                    PARAM_ThingMagicCustomAirProtocols airProtocol = new PARAM_ThingMagicCustomAirProtocols();
                    airProtocol.customProtocolId = ENUM_ThingMagicCustomAirProtocolList.IPX64;
                    inventoryParam.Custom.Add(airProtocol);
                }
                else if (TagProtocol.IPX256 == protocol)
                {
                    inventoryParam.ProtocolID = ENUM_AirProtocols.Unspecified;
                    PARAM_ThingMagicCustomAirProtocols airProtocol = new PARAM_ThingMagicCustomAirProtocols();
                    airProtocol.customProtocolId = ENUM_ThingMagicCustomAirProtocolList.IPX256;
                    inventoryParam.Custom.Add(airProtocol);
                }
                else if (TagProtocol.ATA == protocol)
                {
                    inventoryParam.ProtocolID = ENUM_AirProtocols.Unspecified;
                    PARAM_ThingMagicCustomAirProtocols airProtocol = new PARAM_ThingMagicCustomAirProtocols();
                    airProtocol.customProtocolId = ENUM_ThingMagicCustomAirProtocolList.ATA;
                    inventoryParam.Custom.Add(airProtocol);
                }
                if (customAntConfig != null)
                {
                    PARAM_ThingMagicCustomAntennaSwitching customAntennaSwitching = new PARAM_ThingMagicCustomAntennaSwitching();
                    if (customAntConfig.antSwitchingType == 1)
                        customAntennaSwitching.AntSwitchingType = ENUM_ThingMagicCustomAntennaSwitchingType.Dynamic;
                    else
                        customAntennaSwitching.AntSwitchingType = ENUM_ThingMagicCustomAntennaSwitchingType.Equal;
                    customAntennaSwitching.Timeout = customAntConfig.tagReadTimeout;
                    inventoryParam.Custom.Add(customAntennaSwitching);
                }
                // Set InventoryParameterSpec id
                inventoryParam.InventoryParameterSpecID = (ushort)(planCount + 1);
                invParamList.Add(inventoryParam);
                aiSpec.InventoryParameterSpec = invParamList.ToArray();

                if ((numPlans > 1) && (perAntOnTimeSupport == true))
                {
                    // Add readPlan weight option as Custom
                    // parameter to the Inventory parameter
                    MultiReadPlan mrp = (MultiReadPlan)rp;
                    ReadPlan[] rplans = mrp.Plans;
                    if (rplans[planCount] is SimpleReadPlan)
                    {
                        SimpleReadPlan srp = (SimpleReadPlan)rplans[planCount];
                        PARAM_ThingMagicCustomReadplanWeight readPlanWt = new PARAM_ThingMagicCustomReadplanWeight();
                        readPlanWt.planWeight = (uint)srp.Weight;
                        readPlanWt.multiPlanWeight = (uint)mrp.TotalWeight;
                        inventoryParam.Custom.Add(readPlanWt);
                    }
                }
                roSpec.SpecParameter.Add(aiSpec);
            }

            if (continuousReading && (statFlag != 0))
            {
                PARAM_RFSurveySpec rfSurveySpec = new PARAM_RFSurveySpec();
                rfSurveySpec.AntennaID = antennaGlobal[0];
                //rfSurveySpec.StartFrequency=
                //Reader.Stat.Values value=(Reader.Stat.Values)GetTMStatsValue(1);
                //rfSurveySpec.AntennaID=value.ANTENNA;
                PARAM_RFSurveySpecStopTrigger trig = new PARAM_RFSurveySpecStopTrigger();
                trig.StopTriggerType = ENUM_RFSurveySpecStopTriggerType.Null;
                rfSurveySpec.RFSurveySpecStopTrigger = trig;
                int[] hopTable = getRegulatoryCapabilities();
                rfSurveySpec.StartFrequency = (uint)hopTable[0];
                rfSurveySpec.EndFrequency = (uint)hopTable[hopTable.Length - 1];

                //string[] ver1 = softwareVersion.Split('.');
                if (statListSupport)//add this "statListSupport" for checking
                {
                    PARAM_CustomRFSurveySpec surveySpec = new PARAM_CustomRFSurveySpec();
                    surveySpec.StatsEnable = (ENUM_ThingMagicCustomStatsEnableFlag)GetTMStatsEnable(1);
                    rfSurveySpec.Custom.Add(surveySpec);
                    roSpec.SpecParameter.Add(rfSurveySpec);
                }
            }


            //Specify Report spec
            roSpec.ROReportSpec = new PARAM_ROReportSpec();
            // Specify what type of tag reports we want to receive and when we want to receive them.
            roSpec.ROReportSpec.ROReportTrigger = ENUM_ROReportTriggerType.Upon_N_Tags_Or_End_Of_ROSpec;

            // Receive a report every time a tag is read.
            if (continuousReading)
            {
                roSpec.ROReportSpec.N = 1;
            }
            else
            {
                roSpec.ROReportSpec.N = 0;
            }
            // Selecting which fields we want in the report.
            roSpec.ROReportSpec.TagReportContentSelector = new PARAM_TagReportContentSelector();

            if (metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
            {
                roSpec.ROReportSpec.TagReportContentSelector.EnableAccessSpecID = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableAntennaID = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableChannelIndex = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableFirstSeenTimestamp = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableInventoryParameterSpecID = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableLastSeenTimestamp = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnablePeakRSSI = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableROSpecID = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableSpecIndex = true;
                roSpec.ROReportSpec.TagReportContentSelector.EnableTagSeenCount = true;
            }
            else
            {
                roSpec.ROReportSpec.TagReportContentSelector.EnableAccessSpecID = true;

                if ((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataAntID) == (ENUM_ThingMagicCustomMetadataFlag.MetadataAntID))
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnableAntennaID = true;
                }
                else
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnableAntennaID = false;
                }
                if ((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataFrequency) == (ENUM_ThingMagicCustomMetadataFlag.MetadataFrequency))
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnableChannelIndex = true;
                }
                else
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnableChannelIndex = false;
                }
                roSpec.ROReportSpec.TagReportContentSelector.EnableFirstSeenTimestamp = true;

                roSpec.ROReportSpec.TagReportContentSelector.EnableInventoryParameterSpecID = true;

                roSpec.ROReportSpec.TagReportContentSelector.EnableLastSeenTimestamp = true;

                if ((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataRSSI) == (ENUM_ThingMagicCustomMetadataFlag.MetadataRSSI))
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnablePeakRSSI = true;
                }
                else
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnablePeakRSSI = false;
                }

                roSpec.ROReportSpec.TagReportContentSelector.EnableROSpecID = true;

                roSpec.ROReportSpec.TagReportContentSelector.EnableSpecIndex = true;
                if ((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataReadCount) == (ENUM_ThingMagicCustomMetadataFlag.MetadataReadCount))
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnableTagSeenCount = true;
                }
                else
                {
                    roSpec.ROReportSpec.TagReportContentSelector.EnableTagSeenCount = false;
                }
            }

            // By default both PC and CRC bits are set, so sent from tmmpd
            PARAM_C1G2EPCMemorySelector gen2MemSelector = new PARAM_C1G2EPCMemorySelector();
            gen2MemSelector.EnableCRC = true;
            gen2MemSelector.EnablePCBits = true;

            roSpec.ROReportSpec.TagReportContentSelector.AirProtocolEPCMemorySelector.Add(gen2MemSelector);
            PARAM_ThingMagicTagReportContentSelector tagReportContentSelector = new PARAM_ThingMagicTagReportContentSelector();
            string[] ver = softwareVersion.Split('.');
            if ((Convert.ToInt32(ver[0]) == 4) && (Convert.ToInt32(ver[1]) >= 17) || (Convert.ToInt32(ver[0]) > 4))
            {
                tagReportContentSelector.PhaseMode = ENUM_ThingMagicPhaseMode.Enabled;
                roSpec.ROReportSpec.AddCustomParameter(tagReportContentSelector);
            }
            // Since Spruce release firmware doesn't support phase, don't add PARAM_ThingMagicTagReportContentSelector 
            // custom paramter in ROReportSpec
            //string[] ver = softwareVersion.Split('.');
            if (customMetaDataSupport)//((Convert.ToInt32(ver[0]) == 4) && (Convert.ToInt32(ver[1]) >= 17)) || (Convert.ToInt32(ver[0]) > 4))
            {
                if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGPIOStatus) == (ENUM_ThingMagicCustomMetadataFlag.MetadataGPIOStatus)) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                {
                    PARAM_MetadataGPIOMode gpio = new PARAM_MetadataGPIOMode();
                    gpio.Mode = ENUM_ThingMagicMetadataFlagStatus.Enabled;
                    tagReportContentSelector.MetadataGPIOMode = gpio;
                    roSpec.ROReportSpec.AddCustomParameter(tagReportContentSelector);
                }

                if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGen2LF) == (ENUM_ThingMagicCustomMetadataFlag.MetadataGen2LF)) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                {
                    PARAM_MetadataGen2LFMode gen2LF = new PARAM_MetadataGen2LFMode();
                    gen2LF.Mode = ENUM_ThingMagicMetadataFlagStatus.Enabled;
                    tagReportContentSelector.MetadataGen2LFMode = gen2LF;
                    roSpec.ROReportSpec.AddCustomParameter(tagReportContentSelector);
                }

                if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Q) == (ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Q)) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                {
                    PARAM_MetadataGen2QMode gen2Q = new PARAM_MetadataGen2QMode();
                    gen2Q.Mode = ENUM_ThingMagicMetadataFlagStatus.Enabled;
                    tagReportContentSelector.MetadataGen2QMode = gen2Q;
                    roSpec.ROReportSpec.AddCustomParameter(tagReportContentSelector);
                }

                if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Target) == (ENUM_ThingMagicCustomMetadataFlag.MetadataGen2Target)) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                {
                    PARAM_MetadataGen2TargetMode gen2Target = new PARAM_MetadataGen2TargetMode();
                    gen2Target.Mode = ENUM_ThingMagicMetadataFlagStatus.Enabled;
                    tagReportContentSelector.MetadataGen2TargetMode = gen2Target;
                    roSpec.ROReportSpec.AddCustomParameter(tagReportContentSelector);
                }

                if (((metadataflag & ENUM_ThingMagicCustomMetadataFlag.MetadataData) == (ENUM_ThingMagicCustomMetadataFlag.MetadataData)) || metadataflag.Equals(ENUM_ThingMagicCustomMetadataFlag.MetadataAll))
                {
                    PARAM_MetadataDataMode metadataDataMode = new PARAM_MetadataDataMode();
                    metadataDataMode.Mode = ENUM_ThingMagicMetadataFlagStatus.Enabled;
                    tagReportContentSelector.MetadataDataMode = metadataDataMode;
                    roSpec.ROReportSpec.AddCustomParameter(tagReportContentSelector);
                }
            }
            roSpecList.Add(roSpec);
        }

        private PARAM_AccessSpec createAccessSpec(SimpleReadPlan srp, bool isStandaloneOp)
        {
            PARAM_AccessCommand accessCommand = new PARAM_AccessCommand();
            PARAM_AccessSpec accessSpec = new PARAM_AccessSpec();
            PARAM_AccessSpecStopTrigger trigger = new PARAM_AccessSpecStopTrigger();

            if (srp.Filter != null && (srp.Op is Gen2.NxpGen2TagOp.EasAlarm))
            {
                throw new FeatureNotSupportedException("NxpEasAlarm with filter is not supported");
            }
            if (srp.Op is TagOpList)
            {
                // builds tagop list and adds to accessCommandOpSpecList
                BuildtagOpListSpec(srp, accessCommand);
            }
            else
            {
                accessCommand.AccessCommandOpSpec.Add(BuildOpSpec(srp));
            }
            accessSpec.AccessSpecID = ++AccessSpecID;
            accessSpec.AccessCommand = accessCommand;
            accessSpec.ROSpecID = roSpecId;

            if (addInvSpecIDSupport == true)
            {
                PARAM_ThingMagicCustomInventorySpecID invSpecID = new PARAM_ThingMagicCustomInventorySpecID();
                invSpecID.InventorySpecId = invSpecId;
                accessSpec.AddCustomParameter(invSpecID);
            }

            if (!isStandaloneOp)//Embedded operation
            {
                if (srp.Op is Gen2.NxpGen2TagOp.EasAlarm)
                {
                    throw new FeatureNotSupportedException("Gen2.NxpGen2TagOp.EasAlarm command can be standalone tag operation ");
                }
                if (srp.Op is Gen2.NXP.G2X.ResetReadProtect)
                {
                    throw new FeatureNotSupportedException("NXP Reset Read protect command can be embedded only if the chip-type is G2il");
                }
                accessSpec.AntennaID = 0;
                trigger.AccessSpecStopTrigger = ENUM_AccessSpecStopTriggerType.Null;
                trigger.OperationCountValue = 0;
            }
            else
            {
                //standalone operation
                if (srp.Op is Gen2.Alien.Higgs2.PartialLoadImage)
                {
                    if (null != srp.Filter)
                    {
                        throw new ReaderException("Filter is not supported on this operation.");
                    }
                }
                accessSpec.AntennaID = Convert.ToUInt16(ParamGet("/reader/tagop/antenna"));
                trigger.AccessSpecStopTrigger = ENUM_AccessSpecStopTriggerType.Operation_Count;
                trigger.OperationCountValue = 1;
            }

            if (srp.Protocol == TagProtocol.GEN2)
            {
                accessSpec.ProtocolID = ENUM_AirProtocols.EPCGlobalClass1Gen2;
            }
            else
            {
                accessSpec.ProtocolID = ENUM_AirProtocols.Unspecified;
            }
            accessSpec.CurrentState = ENUM_AccessSpecState.Disabled;
            accessSpec.AccessSpecStopTrigger = trigger;

            // Add a list of target tags to the tag spec.
            PARAM_C1G2TagSpec tagSpec = new PARAM_C1G2TagSpec();
            PARAM_C1G2TargetTag targetTag = new PARAM_C1G2TargetTag();
            targetTag.MB = new LTKD.TwoBits(0);
            targetTag.Match = false;
            targetTag.Pointer = 0;
            //targetTag.TagData = LTKD.LLRPBitArray.FromBinString("0");
            //targetTag.TagMask = LTKD.LLRPBitArray.FromBinString("0");

            List<PARAM_C1G2TargetTag> targetTagList = new List<PARAM_C1G2TargetTag>();
            targetTagList.Add(targetTag);
            tagSpec.C1G2TargetTag = targetTagList.ToArray();

            //Add the tag spec to the access command.
            accessCommand.AirProtocolTagSpec.Add(tagSpec);
            return accessSpec;
        }
        bool processData = false;
        private bool DeleteRoSpec()
        {
            try
            {
                MSG_DELETE_ROSPEC msg = new MSG_DELETE_ROSPEC();
                msg.ROSpecID = 0;
                MSG_DELETE_ROSPEC_RESPONSE response = (MSG_DELETE_ROSPEC_RESPONSE)SendLlrpMessage(msg);

                if (null != response)
                {
                    if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                    {

                        if (tagReadQueue.Count != 0)
                            tagReadQueue.Dequeue();
                        if (RFSurvyQueue.Count != 0)
                        {
                            RFSurvyQueue.Dequeue();
                        }
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }

        private bool EnableRoSpec(uint specId)
        {
            try
            {
                MSG_ENABLE_ROSPEC msg = new MSG_ENABLE_ROSPEC();
                msg.ROSpecID = specId;

                MSG_ENABLE_ROSPEC_RESPONSE response = (MSG_ENABLE_ROSPEC_RESPONSE)SendLlrpMessage(msg);

                if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }

        }

        private bool StartRoSpec(uint specId)
        {
            try
            {
                MSG_START_ROSPEC msg = new MSG_START_ROSPEC();
                msg.ROSpecID = specId;
                MSG_START_ROSPEC_RESPONSE response = (MSG_START_ROSPEC_RESPONSE)SendLlrpMessage(msg);
                if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                {
                    processData = true;
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }

        private string StopRoSpec(uint specId)
        {
            string response = string.Empty;
            MSG_STOP_ROSPEC msg = new MSG_STOP_ROSPEC();
            msg.ROSpecID = specId;

            MSG_STOP_ROSPEC_RESPONSE rsp = (MSG_STOP_ROSPEC_RESPONSE)SendLlrpMessage(msg);

            if (rsp != null)
            {
                response = rsp.ToString();
            }
            else if (errorMessage != null)
            {
                response = errorMessage.ToString();
            }
            else
            {
                response = "Command time out!";
            }
            return response;
        }

        #region SimpleTransportListener

        /// <summary>
        /// Simple console-output transport listener
        /// </summary>
        public override void SimpleTransportListener(Object sender, TransportListenerEventArgs e)
        {
            string msg;
            msg = Encoding.ASCII.GetString(e.Data, 0, e.Data.Length);
            Console.WriteLine(String.Format(
                "{0}: {1} (timeout={2:D}ms)",
                e.Tx ? "TX" : "RX",
                msg,
                e.Timeout
                ));
        }

        private void BuildTransport(bool tx, LTKD.Message msg)
        {
            if (null != msg)
            {
                OnTransport(tx, Encoding.ASCII.GetBytes(msg.ToString()), 1000);
            }
        }
        #endregion SimpleTransportListener


        #region Private Methods

        /// <summary>
        /// Get Reader Description
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetReaderDescription(Object val)
        {
            string readerDesc = null;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgrsp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicReaderConfiguration);
                PARAM_ThingMagicReaderConfiguration par = (PARAM_ThingMagicReaderConfiguration)msgrsp.Custom[0];
                readerDesc = par.ReaderDescription;
            }
            catch (Exception ex)
            {
                if (ex is FeatureNotSupportedException)
                {
                    readerDesc = string.Empty;
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
            return readerDesc;
        }

        /// <summary>
        /// Set Reader Description
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetReaderDescription(Object val)
        {
            string readRole = string.Empty;
            string readerHostName = string.Empty;
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            PARAM_ThingMagicReaderConfiguration rd = new PARAM_ThingMagicReaderConfiguration();
            MSG_GET_READER_CONFIG msgGetConfig = new MSG_GET_READER_CONFIG();
            msgGetConfig.RequestedData = ENUM_GetReaderConfigRequestedData.Identification;
            PARAM_ThingMagicDeviceControlConfiguration deviceConfig = new PARAM_ThingMagicDeviceControlConfiguration();
            deviceConfig.RequestedData = ENUM_ThingMagicControlConfiguration.ThingMagicReaderConfiguration;
            msgGetConfig.AddCustomParameter(deviceConfig);
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgGetConfig);
                PARAM_ThingMagicReaderConfiguration par = (PARAM_ThingMagicReaderConfiguration)msgGetConfigResp.Custom[0];
                readRole = par.ReaderRole;
                readerHostName = par.ReaderHostName;
            }
            catch (Exception ex)
            {
                {
                    throw new ReaderException(ex.Message);
                }
            }
            string value = (string)val;
            if (value.Length > 128)
            {
                throw new ReaderException("Reader Description length should be less then 128 characters");
            }
            rd.ReaderDescription = value;
            rd.ReaderRole = readRole;
            rd.ReaderHostName = readerHostName;
            msgSetConfig.AddCustomParameter(rd);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Reader HostName
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetReaderHostName(Object val)
        {
            string readerHostName = null;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgrsp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicReaderConfiguration);
                PARAM_ThingMagicReaderConfiguration par = (PARAM_ThingMagicReaderConfiguration)msgrsp.Custom[0];
                readerHostName = par.ReaderHostName;
            }
            catch (Exception ex)
            {
                if (ex is FeatureNotSupportedException)
                {
                    readerHostName = string.Empty;
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
            return readerHostName;
        }

        /// <summary>
        /// Set Reader HostName
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetReaderHostName(Object val)
        {
            string readerDescription = string.Empty;
            string readerRole = string.Empty;
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            PARAM_ThingMagicReaderConfiguration rd = new PARAM_ThingMagicReaderConfiguration();
            MSG_GET_READER_CONFIG msgGetConfig = new MSG_GET_READER_CONFIG();
            msgGetConfig.RequestedData = ENUM_GetReaderConfigRequestedData.Identification;
            PARAM_ThingMagicDeviceControlConfiguration deviceConfig = new PARAM_ThingMagicDeviceControlConfiguration();
            deviceConfig.RequestedData = ENUM_ThingMagicControlConfiguration.ThingMagicReaderConfiguration;
            msgGetConfig.AddCustomParameter(deviceConfig);
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgGetConfig);
                PARAM_ThingMagicReaderConfiguration par = (PARAM_ThingMagicReaderConfiguration)msgGetConfigResp.Custom[0];
                readerDescription = par.ReaderDescription;
                readerRole = par.ReaderRole;
            }
            catch (Exception ex)
            {
                {
                    throw new ReaderException(ex.Message);
                }
            }
            string value = (string)val;
            rd.ReaderHostName = value;
            rd.ReaderDescription = readerDescription;
            rd.ReaderRole = readerRole;
            msgSetConfig.AddCustomParameter(rd);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Regulatory Mode
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetRegulatoryMode(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicRegulatoryConfiguration regConfig = new PARAM_ThingMagicRegulatoryConfiguration();
            PARAM_RegulatoryMode regMode = new PARAM_RegulatoryMode();
            regMode.ModeParam = (ENUM_ThingMagicRegulatoryMode)val;
            regConfig.RegulatoryMode = regMode;
            msgSetConfig.AddCustomParameter(regConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Regulatory Modulation
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetRegulatoryModulation(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicRegulatoryConfiguration regConfig = new PARAM_ThingMagicRegulatoryConfiguration();
            PARAM_RegulatoryModulation regModulation = new PARAM_RegulatoryModulation();
            regModulation.ModulationParam = (ENUM_ThingMagicRegulatoryModulation)val;
            regConfig.RegulatoryModulation = regModulation;
            msgSetConfig.AddCustomParameter(regConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Regulatory On time
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetRegulatoryonTime(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicRegulatoryConfiguration regConfig = new PARAM_ThingMagicRegulatoryConfiguration();
            PARAM_RegulatoryOntime regOnTime = new PARAM_RegulatoryOntime();
            regConfig.RegulatoryOntime = regOnTime;
            regConfig.RegulatoryOntime.OntimeParam = Convert.ToUInt16(val);
            msgSetConfig.AddCustomParameter(regConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Regulatory Off time
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetRegulatoryOffTime(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicRegulatoryConfiguration regConfig = new PARAM_ThingMagicRegulatoryConfiguration();
            PARAM_RegulatoryOfftime regOffTime = new PARAM_RegulatoryOfftime();
            regConfig.RegulatoryOfftime = regOffTime;
            regConfig.RegulatoryOfftime.OfftimeParam = Convert.ToUInt16(val);
            msgSetConfig.AddCustomParameter(regConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Regulatory Enable
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetRegulatoryEnable(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicRegulatoryConfiguration regConfig = new PARAM_ThingMagicRegulatoryConfiguration();
            PARAM_RegulatoryEnable regEnable = new PARAM_RegulatoryEnable();
            regConfig.RegulatoryEnable = regEnable;
            regConfig.RegulatoryEnable.EnableParam = Convert.ToBoolean(val);
            msgSetConfig.AddCustomParameter(regConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Region HopTable
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetRegionHopTable(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicFrequencyConfiguration freqConfig = new PARAM_ThingMagicFrequencyConfiguration();
            freqConfig.Hopping = true;

            PARAM_CustomFrequencyHopTable custFreqHopTable = new PARAM_CustomFrequencyHopTable();
            custFreqHopTable.HopTableID = 1;

            int[] hopTableList = (int[])val;
            List<UInt32> data = new List<UInt32>();
            foreach (int hopfreq in hopTableList)
            {
                data.Add(Convert.ToUInt32(hopfreq));
            }

            Org.LLRP.LTK.LLRPV1.DataType.UInt32Array frequency = new Org.LLRP.LTK.LLRPV1.DataType.UInt32Array();
            frequency.data = data;
            custFreqHopTable.Frequency = frequency;

            PARAM_CustomFrequencyHopTable[] hopList = new PARAM_CustomFrequencyHopTable[hopTableList.Length];
            List<PARAM_CustomFrequencyHopTable> customFrequencyHopTableList = new List<PARAM_CustomFrequencyHopTable>();
            customFrequencyHopTableList.Add(custFreqHopTable);

            freqConfig.CustomFrequencyHopTable = customFrequencyHopTableList.ToArray();
            msgSetConfig.AddCustomParameter(freqConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Regulatory capabilities
        /// </summary>
        /// <returns>int[]</returns>
        private int[] getRegulatoryCapabilities()
        {
            MSG_GET_READER_CAPABILITIES msgCapab = new MSG_GET_READER_CAPABILITIES();
            MSG_GET_READER_CAPABILITIES_RESPONSE msgCapabRes;
            // Regulatory capabilities are available as part of Reader Capabilities
            try
            {
                msgCapab.RequestedData = ENUM_GetReaderCapabilitiesRequestedData.Regulatory_Capabilities;
                msgCapabRes = (MSG_GET_READER_CAPABILITIES_RESPONSE)SendLlrpMessage(msgCapab);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }

            //cache frequency hop table
            frequencyHopTable = msgCapabRes.RegulatoryCapabilities.UHFBandCapabilities.FrequencyInformation.FrequencyHopTable;

            //Extract the frequency hoptable
            freq = frequencyHopTable[0].Frequency.data;

            int[] freqHopTable = new int[freq.Count];
            for (int j = 0; j < freq.Count; j++)
            {
                freqHopTable[j] = Convert.ToInt32(freq[j]);
            }
            return freqHopTable;
        }

        /// <summary>
        /// Get Regulatory Mode
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetRegulatoryMode(Object val)
        {
            ENUM_ThingMagicRegulatoryMode mode;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicRegulatoryConfiguration);
                PARAM_ThingMagicRegulatoryConfiguration regConfig = (PARAM_ThingMagicRegulatoryConfiguration)msgGetConfigResp.Custom[0];
                mode = (ENUM_ThingMagicRegulatoryMode)regConfig.RegulatoryMode.ModeParam;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return mode;
        }

        /// <summary>
        /// Get Regulatory Modulation
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetRegulatoryModulation(Object val)
        {
            ENUM_ThingMagicRegulatoryModulation modulation;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicRegulatoryConfiguration);
                PARAM_ThingMagicRegulatoryConfiguration regConfig = (PARAM_ThingMagicRegulatoryConfiguration)msgGetConfigResp.Custom[0];
                modulation = (ENUM_ThingMagicRegulatoryModulation)regConfig.RegulatoryModulation.ModulationParam;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return modulation;
        }

        /// <summary>
        /// Get Regulatory ontime
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetRegulatoryOnTime(Object val)
        {
            UInt16 regOnTime;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicRegulatoryConfiguration);
                PARAM_ThingMagicRegulatoryConfiguration regConfig = (PARAM_ThingMagicRegulatoryConfiguration)msgGetConfigResp.Custom[0];
                regOnTime = Convert.ToUInt16(regConfig.RegulatoryOntime.OntimeParam);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return regOnTime;
        }

        /// <summary>
        /// Get Regulatory offtime
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetRegulatoryOffTime(Object val)
        {
            UInt16 regOffTime;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicRegulatoryConfiguration);
                PARAM_ThingMagicRegulatoryConfiguration regConfig = (PARAM_ThingMagicRegulatoryConfiguration)msgGetConfigResp.Custom[0];
                regOffTime = Convert.ToUInt16(regConfig.RegulatoryOfftime.OfftimeParam);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return regOffTime;
        }

        /// <summary>
        /// Get Antenna return loss value
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetAntennaReturnLoss(Object val)
        {
            List<int[]> arlList = new List<int[]>();
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicAntennaReturnloss);
                PARAM_ThingMagicAntennaReturnloss custAntRetLoss = (PARAM_ThingMagicAntennaReturnloss)msgGetConfigResp.Custom[0];
                PARAM_ReturnlossValue[] antRetLoss = (PARAM_ReturnlossValue[])custAntRetLoss.ReturnlossValue;

                for (int count = 0; count < antRetLoss.Length; count++)
                {
                    arlList.Add(new int[] { Convert.ToByte(antRetLoss[count].Port), Convert.ToInt32(antRetLoss[count].Value) });
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return arlList.ToArray();
        }
        

       
        private object GetReaderMetadata(Object val)
        {
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgrsp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicMetadata);
                PARAM_ThingMagicMetadata par = (PARAM_ThingMagicMetadata)msgrsp.Custom[0];
                metadataflag = (ENUM_ThingMagicCustomMetadataFlag)par.Metadata;
            }
            catch (Exception ex)
            {
                if (ex is FeatureNotSupportedException)
                {
                    metadataflag = 0;
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
            return metadataflag;
        }


        private object SetReaderMetadata(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicMetadata readerMetadata = new PARAM_ThingMagicMetadata();
            readerMetadata.Metadata = metadataflag = (ENUM_ThingMagicCustomMetadataFlag)val;
            String metadataStr = val.ToString();
            if (metadataStr.IndexOf("TAGTYPE") != -1)
            {
                throw new Exception("Invalid Argument in \"/reader/metadata\" : " + metadataStr);
            }
            msgSetConfig.AddCustomParameter(readerMetadata);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        new ENUM_ThingMagicCustomStatsEnableFlag statFlag = 0;
        //GetTMStatsEnable
        private object GetTMStatsEnable(Object val)
        {
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgrsp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicStatsEnable);
                PARAM_ThingMagicStatsEnable par = (PARAM_ThingMagicStatsEnable)msgrsp.Custom[0];
                statFlag = (ENUM_ThingMagicCustomStatsEnableFlag)par.StatsEnable;
            }
            catch (Exception ex)
            {
                if (ex is FeatureNotSupportedException)
                {
                    statFlag = 0;
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
            return statFlag;
        }

        //SetTMStatsEnable
        private object SetTMStatsEnable(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;
            PARAM_ThingMagicStatsEnable statsdata = new PARAM_ThingMagicStatsEnable();
            statFlag = statsdata.StatsEnable = (ENUM_ThingMagicCustomStatsEnableFlag)val;
            msgSetConfig.AddCustomParameter(statsdata);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        //GetTMStatsValue
        private object GetTMStatsValue(Object val)
        {
            PARAM_CustomStatsValue Statsflag = null;
            Reader.Stat.Values valss = new Reader.Stat.Values();
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgrsp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicReaderStats);
                PARAM_ThingMagicReaderStats par = (PARAM_ThingMagicReaderStats)msgrsp.Custom[0];
                Statsflag = (PARAM_CustomStatsValue)par.CustomStatsValue;
                //Statsflag = Convert.ToUInt16(metadataflag);
                if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableAntennaPorts) == statFlag)
                {
                    valss.ANTENNA = par.CustomStatsValue.AntennaParam.Antenna;
                }
                if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableConnectedAntennas) == statFlag && par.CustomStatsValue.ConnectedAntennaList != null)
                {
                    //uint[] data = new uint[par.CustomStatsValue.ConnectedAntennaList.connectedAntennas.Count];
                    //for (ushort objIndex = 0; objIndex < data.Length; ++objIndex)
                    //{
                    //    ushort length = sizeof(UInt16);
                    //    data[objIndex] = (ushort)((ushort)(par.CustomStatsValue.ConnectedAntennaList.connectedAntennas[objIndex * length] << (ushort)8) +
                    //                                             par.CustomStatsValue.ConnectedAntennaList.connectedAntennas[objIndex + 1]);
                    //}
                    //valss.CONNECTEDANTENNA = data;

                    valss.totalAntennaCount = (par.CustomStatsValue.ConnectedAntennaList.connectedAntennas.Count / 2);
                    List<uint> data1 = new List<uint>();
                    for (int i = 0; i < par.CustomStatsValue.ConnectedAntennaList.connectedAntennas.Count; i++)
                    {
                        i++;
                        if (par.CustomStatsValue.ConnectedAntennaList.connectedAntennas[i] == 1)
                        {
                            data1.Add(par.CustomStatsValue.ConnectedAntennaList.connectedAntennas[(i - 1)]);
                        }
                    }
                    valss.CONNECTEDANTENNA = data1.ToArray();
                }

                if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableFrequency) == statFlag && par.CustomStatsValue.FrequencyParam != null)
                {
                    valss.FREQUENCY = par.CustomStatsValue.FrequencyParam.Frequency;
                }

                if (par.CustomStatsValue.perAntennaStatsList != null)
                {
                    Stat.PerAntennaValues otherval = null;
                    List<Stat.PerAntennaValues> tempdata = new List<Stat.PerAntennaValues>();
                    for (int i = 0; i < par.CustomStatsValue.perAntennaStatsList.Length; i++)
                    {
                        otherval = new Stat.PerAntennaValues();
                        if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableAntennaPorts) == statFlag)
                            otherval.Antenna = par.CustomStatsValue.perAntennaStatsList[i].antenna;
                        if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableNoiseFloorSearchRxTxWithTxOn) == statFlag)
                            otherval.NoiseFloor = par.CustomStatsValue.perAntennaStatsList[i].NoiseFloorParam.noiseFloor;
                        if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableRFOnTime) == statFlag)
                            otherval.RfOnTime = par.CustomStatsValue.perAntennaStatsList[i].RFOntimeParam.rfOntime;
                        tempdata.Add(otherval);
                    }
                    valss.PERANTENNA = tempdata;
                }

                if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableProtocol) == statFlag && par.CustomStatsValue.ProtocolParam != null)
                {
                    valss.PROTOCOL = (TagProtocol)par.CustomStatsValue.ProtocolParam.Protocol;
                }
                if ((statFlag | ENUM_ThingMagicCustomStatsEnableFlag.StatsEnableTemperature) == statFlag)
                {
                    valss.TEMPERATURE = (SByte)par.CustomStatsValue.TemperatureParam.Temperature;
                }
                valss.VALID = (Stat.StatsFlag)statFlag;
                //valss.RESETREADERSTATS = par.CustomStatsValue.StatsEnable;
            }
            catch (Exception ex)
            {
                if (ex is FeatureNotSupportedException)
                {
                    valss = null;
                }
                else
                {
                    throw new ReaderException(ex.Message);
                }
            }
            return valss;
        }


        private object SetThingMagicPortSwitchGPOs(object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigRes = new MSG_SET_READER_CONFIG_RESPONSE();
            try
            {
                PARAM_ThingMagicPortSwitchGPO portSwitchGPO = new PARAM_ThingMagicPortSwitchGPO();
                int[] intArray = (int[])val;
                List<byte> result = new List<byte>();
                foreach (int var in intArray)
                {
                    result.Add((byte)var);
                }
                string key = ByteFormat.ToHex(result.ToArray()).Split('x')[1];
                portSwitchGPO.portSwitchGPOList = LTKD.ByteArray.FromHexString(key);
                msgSetConfig.AddCustomParameter(portSwitchGPO);
                msgSetConfigRes = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
                if (msgSetConfigRes.LLRPStatus.StatusCode != ENUM_StatusCode.M_Success)
                {
                    throw new ReaderException(msgSetConfigRes.LLRPStatus.ErrorDescription.ToString());
                }
                // updating antenna portlist
                InitializeAntennaList();
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }

            return val;
        }

        /// <summary>
        /// Get Antenna Port list
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetAntennaPortList(object val)
        {
            MSG_GET_READER_CONFIG_RESPONSE msgConfigResp;
            try
            {
                msgConfigResp = GetReaderConfigResponse(ENUM_GetReaderConfigRequestedData.AntennaProperties);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            List<int> list = new List<int>();
            PARAM_AntennaProperties[] ant = msgConfigResp.AntennaProperties;
            for (int port = 0; port < ant.Length; port++)
            {
                int count = Convert.ToInt32(msgConfigResp.AntennaProperties[port].AntennaID);
                list.Add(count);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Get Connected Port List
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetConnectedPortList(Object val)
        {
            MSG_GET_READER_CONFIG_RESPONSE msgConfigResp;
            try
            {
                msgConfigResp = GetReaderConfigResponse(ENUM_GetReaderConfigRequestedData.AntennaProperties);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            List<int> list = new List<int>();
            PARAM_AntennaProperties[] ant = msgConfigResp.AntennaProperties;
            for (int port = 0; port < ant.Length; port++)
            {
                if (msgConfigResp.AntennaProperties[port].AntennaConnected)
                {
                    try
                    {
                        int value = Convert.ToInt32(port + 1);
                        list.Add(value);
                    }
                    catch (Exception ex)
                    {
                        throw new ReaderException(ex.Message);
                    }
                }
            }
            return list.ToArray();
        }

        private object SetReadTransmitPowerList(Object val)
        {
            int power = 0;
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigRes = new MSG_SET_READER_CONFIG_RESPONSE();
            List<PARAM_AntennaConfiguration> ac = new List<PARAM_AntennaConfiguration>();
            int[][] prpListValues = (int[][])val;
            foreach (int[] row in prpListValues)
            {
                PARAM_AntennaConfiguration pa = new PARAM_AntennaConfiguration();
                PARAM_RFTransmitter prf = new PARAM_RFTransmitter();
                if ((row[0] > 0))
                {
                    power = row[1];
                    ValidatePowerLevel(power);
                    pa.AntennaID = (ushort)row[0];
                    if ((model.Equals("Astra-EX") || model.Equals("Astra200")) && (regionId.Equals(Region.NA)) && (pa.AntennaID == 1))
                    {
                        if (power > 3000)
                        {
                            throw new ArgumentOutOfRangeException(String.Format("Requested power ({0:D}) too high (RFPowerMax={1:D}cdBm)", power, 3000));
                        }
                    }
                    if (model.Equals("Astra200") && (regionId.Equals(Region.EU)) && (pa.AntennaID == 1))
                    {
                        if (power > 2900)
                        {
                            throw new ArgumentOutOfRangeException(String.Format("Requested power ({0:D}) too high (RFPowerMax={1:D}cdBm)", power, 2900));
                        }
                    }
                    List<int> pwrValueList = new List<int>();
                    foreach (DictionaryEntry pwrEntry in PowerIndexTable)
                    {
                        pwrValueList.Add(Convert.ToInt32(pwrEntry.Key));
                    }
                    int[] pwrValue = pwrValueList.ToArray();
                    Array.Sort(pwrValue);
                    if (true == PowerIndexTable.ContainsKey(Convert.ToInt16(power)))
                    {
                        prf.TransmitPower = Convert.ToUInt16(PowerIndexTable[Convert.ToInt16(power)]);
                    }
                    else
                    {
                        power = RoundOffPowerLevel(power, pwrValue);
                        prf.TransmitPower = Convert.ToUInt16(PowerIndexTable[Convert.ToInt16(power)]);
                    }
                    pa.RFTransmitter = prf;
                    ac.Add(pa);
                }
                else
                {
                    throw new ArgumentOutOfRangeException("Antenna id is invalid");
                }
            }
            msgSetConfig.AntennaConfiguration = ac.ToArray();
            try
            {
                msgSetConfigRes = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        private object GetWriteTransmitPowerList(Object val)
        {
            List<int[]> prpList = new List<int[]>();
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicAntennaConfiguration);
                for (int count = 0; count < antennaMax; count++)
                {
                    ushort writeTransmitPower = ((PARAM_ThingMagicAntennaConfiguration)msgGetConfigResp.Custom[count]).WriteTransmitPower.WriteTransmitPower;
                    prpList.Add(new int[] { ((PARAM_ThingMagicAntennaConfiguration)msgGetConfigResp.Custom[count]).AntennaID, Convert.ToInt32(PowerValueTable[writeTransmitPower]) });
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return prpList.ToArray();
        }

        /// <summary>
        /// Get Read Transmit PowerList
        /// </summary>
        /// <returns>Object</returns>
        private object GetReadTransmitPowerList(Object val)
        {
            List<int[]> prpList = new List<int[]>();
            MSG_GET_READER_CONFIG_RESPONSE msgConfigResp;
            try
            {
                msgConfigResp = GetReaderConfigResponse(ENUM_GetReaderConfigRequestedData.AntennaConfiguration);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            PARAM_AntennaConfiguration[] antConfig = msgConfigResp.AntennaConfiguration;
            for (int count = 0; count < antConfig.Length; count++)
            {
                ushort readTransmitPower = antConfig[count].RFTransmitter.TransmitPower;
                prpList.Add(new int[] { antConfig[count].AntennaID, Convert.ToInt32(PowerValueTable[readTransmitPower]) });
            }
            return val = prpList.ToArray();
        }

        /// <summary>
        /// Get Serial Version
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetVersionSerial(Object val)
        {
            string serialver = string.Empty;
            try
            {
                MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabilitiesResp = GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities.DeviceInformationCapabilities);
                PARAM_DeviceInformationCapabilities paramDeviceInfo = (PARAM_DeviceInformationCapabilities)msgGetCapabilitiesResp.Custom[0];
                serialver = paramDeviceInfo.ReaderSerialNumber;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            val = serialver;
            return val;
        }

        /// <summary>
        /// Get Hardware Version
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetVersionHardware(Object val)
        {
            string hwver = string.Empty;
            try
            {
                MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabilitiesResp = GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities.DeviceInformationCapabilities);
                PARAM_DeviceInformationCapabilities paramDeviceInfo = (PARAM_DeviceInformationCapabilities)msgGetCapabilitiesResp.Custom[0];
                hwver = paramDeviceInfo.HardwareVersion;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            val = hwver;
            return val;
        }

        /// <summary>
        /// Get Product ID
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetProductID(Object val)
        {
            int prodID;
            try
            {
                MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabilitiesResp = GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities.DeviceInformationCapabilities);
                PARAM_DeviceInformationCapabilities paramDeviceInfo = (PARAM_DeviceInformationCapabilities)msgGetCapabilitiesResp.Custom[0];
                prodID = paramDeviceInfo.ReaderProductID.ProductID;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            val = prodID;
            return val;
        }


        /// <summary>
        /// Get Product Group
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetProductGroup(Object val)
        {
            string prodGroup = string.Empty;
            try
            {
                MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabilitiesResp = GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities.DeviceInformationCapabilities);
                PARAM_DeviceInformationCapabilities paramDeviceInfo = (PARAM_DeviceInformationCapabilities)msgGetCapabilitiesResp.Custom[0];
                prodGroup = paramDeviceInfo.ReaderProductGroup.ProductGroup;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            val = prodGroup;
            return val;
        }

        /// <summary>
        /// Get Product Group ID
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetProductGroupID(Object val)
        {
            int prodGroupID;
            try
            {
                MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabilitiesResp = GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities.DeviceInformationCapabilities);
                PARAM_DeviceInformationCapabilities paramDeviceInfo = (PARAM_DeviceInformationCapabilities)msgGetCapabilitiesResp.Custom[0];
                prodGroupID = paramDeviceInfo.ReaderProductGroupID.ProductGroupID;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            val = prodGroupID;
            return val;
        }

        /// <summary>
        /// Get Current Time
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetCurrentTime(Object val)
        {
            string currentTime = string.Empty;
            try
            {
                //get Current time from the reader.
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicCurrentTime);
                PARAM_ThingMagicCurrentTime par = (PARAM_ThingMagicCurrentTime)msgGetConfigResp.Custom[0];
                currentTime = par.ReaderCurrentTime;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return Convert.ToDateTime(currentTime);
        }

        /// <summary>
        /// Get ThingMagic DeDuplication fields such as RecordHighestRssi, UniqueByAntenna and UniqueByData.  
        /// </summary>
        /// <param name="val"></param>
        /// <param name="deDuplicationfield"></param>
        /// <returns>object</returns>
        private object GetThingMagicDeDuplicationFields(Object val, ThingMagicDeDuplication deDuplicationfield)
        {
            MSG_GET_READER_CONFIG msgGetConfig = new MSG_GET_READER_CONFIG();
            msgGetConfig.RequestedData = ENUM_GetReaderConfigRequestedData.Identification;
            PARAM_ThingMagicDeviceControlConfiguration deviceConfig = new PARAM_ThingMagicDeviceControlConfiguration();
            deviceConfig.RequestedData = ENUM_ThingMagicControlConfiguration.ThingMagicDeDuplication;
            msgGetConfig.AddCustomParameter(deviceConfig);
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgGetConfig);
                PARAM_ThingMagicDeDuplication Deduplication = (PARAM_ThingMagicDeDuplication)msgGetConfigResp.Custom[0];
                switch (deDuplicationfield)
                {
                    case ThingMagicDeDuplication.RecordHighestRssi:
                        val = Deduplication.RecordHighestRSSI;
                        break;
                    case ThingMagicDeDuplication.UniqueByAntenna:
                        val = Deduplication.UniqueByAntenna;
                        break;
                    case ThingMagicDeDuplication.UniqueByData:
                        val = Deduplication.UniqueByData;
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set ThingMagic DeDuplication fields such as RecordHighestRssi, UniqueByAntenna and UniqueByData.  
        /// </summary>
        /// <param name="val"></param>
        /// <param name="deDuplicationfield"></param>
        /// <returns>object</returns>
        private object SetThingMagicDeDuplicationFields(Object val, ThingMagicDeDuplication deDuplicationfield)
        {
            MSG_GET_READER_CONFIG msgGetConfig = new MSG_GET_READER_CONFIG();
            MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp;
            PARAM_ThingMagicDeDuplication defaultdeDuplicationFields;
            msgGetConfig.RequestedData = ENUM_GetReaderConfigRequestedData.Identification;
            PARAM_ThingMagicDeviceControlConfiguration deviceConfig = new PARAM_ThingMagicDeviceControlConfiguration();
            deviceConfig.RequestedData = ENUM_ThingMagicControlConfiguration.ThingMagicDeDuplication;
            msgGetConfig.AddCustomParameter(deviceConfig);
            try
            {
                msgGetConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgGetConfig);
                defaultdeDuplicationFields = (PARAM_ThingMagicDeDuplication)msgGetConfigResp.Custom[0];
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            try
            {
                switch (deDuplicationfield)
                {
                    case ThingMagicDeDuplication.RecordHighestRssi:
                        defaultdeDuplicationFields.RecordHighestRSSI = (bool)val;
                        break;
                    case ThingMagicDeDuplication.UniqueByAntenna:
                        defaultdeDuplicationFields.UniqueByAntenna = (bool)val;
                        break;
                    case ThingMagicDeDuplication.UniqueByData:
                        defaultdeDuplicationFields.UniqueByData = (bool)val;
                        break;
                }
                msgSetConfig.AddCustomParameter(defaultdeDuplicationFields);
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get reader module temperature
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetReaderModuleTemperature(Object val)
        {
            MSG_GET_READER_CONFIG_RESPONSE msgConfigResp;
            try
            {
                //Get reader temperature.
                msgConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicReaderModuleTemperature);
                PARAM_ThingMagicReaderModuleTemperature parTemp = (PARAM_ThingMagicReaderModuleTemperature)msgConfigResp.Custom[0];
                val = Convert.ToInt32(parTemp.ReaderModuleTemperature);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Gen2Param such as Gen2 LinkFrequency and TagEncoding.
        /// </summary>
        /// <param name="param"></param>
        /// <returns>object</returns>
        private object GetGen2Param(string param)
        {
            object val = null;
            PARAM_C1G2UHFRFModeTableEntry rfMode = null;
            PARAM_C1G2RFControl rfControl = GetRFcontrol();

            try
            {
                foreach (DictionaryEntry item in RFModeCache)
                {
                    if (rfControl.ModeIndex == Convert.ToInt32(item.Key))
                    {
                        rfMode = (PARAM_C1G2UHFRFModeTableEntry)item.Value;
                    }
                }
                if (null != rfMode)
                {
                    switch (param)
                    {
                        case "TagEncoding":
                            int tagEncoding = Convert.ToInt32(rfMode.MValue);
                            val = tagEncoding;
                            break;
                        case "LinkFrequency":
                            int frequency = Convert.ToInt32(rfMode.BDRValue);
                            val = frequency;
                            break;
                    }
                }
                return val;
            }
            finally
            {
                rfMode = null;
                rfControl = null;
            }
        }

        private object GetPortPowerList(Object val, ThingMagicPower type)
        {
            int[][] pwrvalue = new int[][] { };
            try
            {
                switch (type)
                {
                    case ThingMagicPower.PortReadPowerList:
                    case ThingMagicPower.PortWritePowerList:
                        List<int[]> prpList = new List<int[]>();
                        if (type.Equals(ThingMagicPower.PortReadPowerList))
                        {
                            pwrvalue = (int[][])GetReadTransmitPowerList(val);
                        }
                        else
                        {
                            pwrvalue = (int[][])GetWriteTransmitPowerList(val);
                        }
                        val = pwrvalue;
                        break;
                    case ThingMagicPower.ReadPower:
                    case ThingMagicPower.WritePower:
                        if (type.Equals(ThingMagicPower.ReadPower))
                        {
                            pwrvalue = (int[][])GetReadTransmitPowerList(val);
                        }
                        else
                        {
                            pwrvalue = (int[][])GetWriteTransmitPowerList(val);
                        }
                        int tempPower = pwrvalue[0][1];
                        for (int count = 0; count < pwrvalue.Length; count++)
                        {
                            if (tempPower != pwrvalue[count][1])
                            {
                                // If different, return undefined value
                                throw new ReaderException("Undefined value");
                                //tempPower = 0;
                                //break;
                            }
                        }
                        val = tempPower;
                        // If all antennas have same power, return that value 
                        //if (Convert.ToBoolean(tempPower) || tempPower >= 0)
                        //{
                        //    val = pwrvalue[0][1];
                        //}
                        //else
                        //{
                        //    // If different, return undefined value
                        //    throw new ReaderException("Undefined value");
                        //}
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        private object SetPortPowerList(Object val, ThingMagicPower type)
        {
            switch (type)
            {
                case ThingMagicPower.PortReadPowerList:
                case ThingMagicPower.PortWritePowerList:
                    if (type.Equals(ThingMagicPower.PortReadPowerList))
                    {
                        SetReadTransmitPowerList(val);
                    }
                    else
                    {
                        SetWriteTransmitPowerList(val);
                    }
                    break;
                case ThingMagicPower.ReadPower:
                case ThingMagicPower.WritePower:
                    List<int[]> prpList = new List<int[]>();
                    foreach (int var in antennaPorts)
                    {
                        prpList.Add(new int[] { var, Convert.ToInt32(val) });
                    }
                    if (type.Equals(ThingMagicPower.ReadPower))
                    {
                        SetReadTransmitPowerList(prpList.ToArray());
                    }
                    else
                    {
                        SetWriteTransmitPowerList(prpList.ToArray());
                    }
                    break;
            }
            return val;
        }

        /// <summary>
        /// Get Antenna CheckPort
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetAntennaDetection(Object val)
        {
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicAntennaDetection);
                PARAM_ThingMagicAntennaDetection antennaDetection = (PARAM_ThingMagicAntennaDetection)msgGetConfigResp.Custom[0];
                val = antennaDetection.AntennaDetection;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }
        /// <summary>
        /// Get Protocol Extension
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetProtocolExtension(Object val)
        {
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicGEN2ProtocolExtension);
                PARAM_ThingMagicGEN2ProtocolExtension protocolExtension = (PARAM_ThingMagicGEN2ProtocolExtension)msgGetConfigResp.Custom[0];
                val = Convert.ToInt32(protocolExtension.GEN2ProtocolExtension);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            object gen2ProtocolExtension = null;
            switch ((int)val)
            {
                case 0:
                    gen2ProtocolExtension = Gen2.ProtocolExtension.LICENSE_NONE;
                    break;
                case 1:
                    gen2ProtocolExtension = Gen2.ProtocolExtension.LICENSE_IAV_DENATRAN;
                    break;
            }
            return (Gen2.ProtocolExtension)gen2ProtocolExtension;
        }

        /// <summary>
        /// Get Async On Time
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetAsyncOnTime(Object val)
        {
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicAsyncONTime);
                PARAM_ThingMagicAsyncONTime asyncOnTime = (PARAM_ThingMagicAsyncONTime)msgGetConfigResp.Custom[0];
                val = asyncOnTime.AsyncONTime;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Async Off Time
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetAsyncOffTime(Object val)
        {
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicAsyncOFFTime);
                PARAM_ThingMagicAsyncOFFTime asyncOffTime = (PARAM_ThingMagicAsyncOFFTime)msgGetConfigResp.Custom[0];
                val = asyncOffTime.AsyncOFFTime;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set TagEncoding
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetTagEncoding(Object val)
        {
            int setM = Convert.ToInt32((Gen2.TagEncoding)(val));
            PARAM_C1G2UHFRFModeTableEntry rfMode = null;
            PARAM_C1G2RFControl rfControl = GetRFcontrol();
            foreach (DictionaryEntry item in RFModeCache)
            {
                if (rfControl.ModeIndex == Convert.ToInt32(item.Key))
                {
                    rfMode = (PARAM_C1G2UHFRFModeTableEntry)item.Value;
                }
            }
            string activeFreq = rfMode.BDRValue.ToString();
            int activeM = Convert.ToInt32(rfMode.MValue);
            // Set only if the active m value is not same
            if ((setM != activeM))
            {
                foreach (DictionaryEntry item in RFModeCache)
                {
                    PARAM_C1G2UHFRFModeTableEntry capList = (PARAM_C1G2UHFRFModeTableEntry)item.Value;
                    if (activeFreq.Equals(capList.BDRValue.ToString()) && capList.MValue.Equals(Enum.Parse(typeof(ENUM_C1G2MValue), setM.ToString(), true)))
                    {
                        rfControl.ModeIndex = Convert.ToUInt16(item.Key);
                        try
                        {
                            SetRFControl(rfControl);
                        }
                        catch (Exception ex)
                        {
                            throw new ReaderException(ex.Message);
                        }
                        return val;
                    }
                }
                /*  If the control comes here, then something went wrong.
                    Could be that the combination of currently active blf value
                    and set M value doesnt match with any entry in RFMode table.
                    Eg: if blf already set to 640, and if user sets M value other
                    than FM0, then throw error
                 */
                throw new ReaderException("Specified RFMode is not supported");
            }
            return val;
        }

        /// <summary>
        /// Set Gen2BLF
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetGen2BLF(Object val)
        {
            Gen2.LinkFrequency blf = (Gen2.LinkFrequency)val;
            PARAM_C1G2UHFRFModeTableEntry rfMode = null;
            PARAM_C1G2RFControl rfControl = GetRFcontrol();
            foreach (DictionaryEntry item in RFModeCache)
            {
                if (rfControl.ModeIndex == Convert.ToInt32(item.Key))
                {
                    rfMode = (PARAM_C1G2UHFRFModeTableEntry)item.Value;
                }
            }
            Gen2.LinkFrequency setblf = (Gen2.LinkFrequency)val;
            Gen2.LinkFrequency activeblf = GetLinkFrequency(rfMode.BDRValue);
            string activeM = rfMode.MValue.ToString();
            // Set only if the active BLF value is not same 
            if (setblf != activeblf)
            {
                foreach (DictionaryEntry item in RFModeCache)
                {
                    PARAM_C1G2UHFRFModeTableEntry capList = (PARAM_C1G2UHFRFModeTableEntry)item.Value;
                    if (capList.BDRValue.Equals((uint)GetLinkFrequencyToInt(setblf)) && capList.MValue.Equals(Enum.Parse(typeof(ENUM_C1G2MValue),
                        activeM.ToString(), true)))
                    {
                        rfControl.ModeIndex = Convert.ToUInt16(item.Key);
                        try
                        {
                            SetRFControl(rfControl);
                        }
                        catch (Exception ex)
                        {
                            throw new ReaderException(ex.Message);
                        }
                        return val;
                    }
                }
                /*
                If the control comes here, then something went wrong.
                Could be that the combination of currently active m value
                and set BLF value doesnt match with any entry in RFMode table.
                Eg: if m already set to M2, and if user sets BLF value other than
                250KHz, then throw error.
                */
                throw new ReaderException("Specified RFMode not supported");
            }
            return val;
        }

        /// <summary>
        /// Set Antenna CheckPort
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetAntennaDetection(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            PARAM_ThingMagicAntennaDetection antennaDetection = new PARAM_ThingMagicAntennaDetection();
            antennaDetection.AntennaDetection = (bool)val;
            msgSetConfig.AddCustomParameter(antennaDetection);
            try
            {
                MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set async on time
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetAsyncOnTime(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            PARAM_ThingMagicAsyncONTime asyncOnTime = new PARAM_ThingMagicAsyncONTime();
            try
            {
                asyncOnTime.AsyncONTime = Convert.ToUInt32(val);
                msgSetConfig.AddCustomParameter(asyncOnTime);
                MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set async off time
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetAsyncOffTime(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            PARAM_ThingMagicAsyncOFFTime asyncOffTime = new PARAM_ThingMagicAsyncOFFTime();
            try
            {
                asyncOffTime.AsyncOFFTime = Convert.ToUInt32(val);
                msgSetConfig.AddCustomParameter(asyncOffTime);
                MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Gen2T4
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetCustomGen2T4(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            PARAM_Gen2T4Param t4Param = new PARAM_Gen2T4Param();
            t4Param.T4ParamValue = Convert.ToUInt32(val);
            msgSetConfig.AddCustomParameter(t4Param);
            PARAM_Gen2CustomParameters custGen = new PARAM_Gen2CustomParameters();
            custGen.Gen2T4Param = t4Param;
            PARAM_ThingMagicProtocolConfiguration pc = new PARAM_ThingMagicProtocolConfiguration();
            pc.Gen2CustomParameters = custGen;
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.AddCustomParameter(pc);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }


        /// <summary>
        /// Get Gen2T4
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private object GetCustomGen2T4(Object val)
        {
            UInt32 t4Param = 0;
            PARAM_ThingMagicProtocolConfiguration pc;

            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicProtocolConfiguration);
                pc = (PARAM_ThingMagicProtocolConfiguration)msgGetConfigResp.Custom[0];
                t4Param = Convert.ToUInt32(pc.Gen2CustomParameters.Gen2T4Param.T4ParamValue);

            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return t4Param;
        }

        private void SetHoldReportsAndEvents(bool objYes)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            PARAM_EventsAndReports eventsAndReports = new PARAM_EventsAndReports();
            eventsAndReports.HoldEventsAndReportsUponReconnect = objYes;
            msgSetConfig.EventsAndReports = eventsAndReports;
            try
            {
                MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }

        private void EnableEventsAndReports()
        {
            MSG_ENABLE_EVENTS_AND_REPORTS eventsAndReports = new MSG_ENABLE_EVENTS_AND_REPORTS();
            SendLlrpMessage(eventsAndReports);
        }

        /// <summary>
        /// Get session
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetSession(Object val)
        {
            MSG_GET_READER_CONFIG msgGetConfig = new MSG_GET_READER_CONFIG();
            MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp;
            msgGetConfig.RequestedData = ENUM_GetReaderConfigRequestedData.AntennaConfiguration;
            try
            {
                msgGetConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgGetConfig);
                //All antenna configurations should have same session.
                //Hence considering only session of antenna Id 1's configuration
                PARAM_AntennaConfiguration[] antennaconfig = msgGetConfigResp.AntennaConfiguration;
                PARAM_C1G2SingulationControl SingulationControl = ((PARAM_C1G2InventoryCommand)(antennaconfig[0].AirProtocolInventoryCommandSettings[0])).C1G2SingulationControl;
                val = SingulationControl.Session.ToInt();
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return Enum.Parse(typeof(Gen2.Session), val.ToString(), true);
        }

        private int GetTariValue(Gen2.Tari value)
        {
            switch (value)
            {
                case Gen2.Tari.TARI_25US:
                    return 25000;
                case Gen2.Tari.TARI_12_5US:
                    return 12500;
                case Gen2.Tari.TARI_6_25US:
                    return 6250;
                default:
                    return 0;
            }
        }

        private Gen2.Tari GetTariEnum(int tari)
        {
            switch (tari)
            {
                case 25000:
                    return Gen2.Tari.TARI_25US;
                case 12500:
                    return Gen2.Tari.TARI_12_5US;
                case 6250:
                    return Gen2.Tari.TARI_6_25US;
                default:
                    return (Gen2.Tari)0;
            }
        }

        /// <summary>
        /// Set License Key
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetThingMagicLicenseKey(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp = null;
            try
            {
                PARAM_ThingMagicLicenseKey licenseKey = new PARAM_ThingMagicLicenseKey();
                string key = ByteFormat.ToHex((byte[])val).Split('x')[1];
                licenseKey.LicenseKey = LTKD.ByteArray.FromHexString(key);
                msgSetConfig.AddCustomParameter(licenseKey);
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
                if (msgSetConfigResp.LLRPStatus.StatusCode != ENUM_StatusCode.M_Success)
                {
                    throw new ReaderException(msgSetConfigResp.LLRPStatus.ErrorDescription.ToString());
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Supported Protocols
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetSupportedProtocols(Object val)
        {
            MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabResp;
            try
            {
                msgGetCapabResp = GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities.DeviceProtocolCapabilities);
                PARAM_DeviceProtocolCapabilities deviceProtocol = (PARAM_DeviceProtocolCapabilities)msgGetCapabResp.Custom[0];
                PARAM_SupportedProtocols[] TMsupportedProtocols = deviceProtocol.SupportedProtocols;

                List<TagProtocol> supportedProtocols = new List<TagProtocol>();
                foreach (PARAM_SupportedProtocols protocol in TMsupportedProtocols)
                {
                    //We know our LLRP readers supports on Gen2, ATA and ISO18000-6B.
                    //So populate supported protocols list with Gen2, ATA and Iso18k6B protocols only.
                    //If ThingMagic:DeviceProtocolCapabilties indicates that either of those protocols is not enabled, don't add it in the supported list.
                    if (TagProtocol.GEN2.Equals((TagProtocol)protocol.Protocol) || TagProtocol.ISO180006B.Equals((TagProtocol)protocol.Protocol) || TagProtocol.ATA.Equals((TagProtocol)protocol.Protocol) || TagProtocol.IPX64.Equals((TagProtocol)protocol.Protocol) || TagProtocol.IPX256.Equals((TagProtocol)protocol.Protocol))
                    {
                        supportedProtocols.Add((TagProtocol)protocol.Protocol);
                    }
                }
                val = supportedProtocols.ToArray();
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Gen2q
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetGen2Q(Object val)
        {
            int initq = 0;
            PARAM_ThingMagicProtocolConfiguration protocolConfig;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicProtocolConfiguration);
                protocolConfig = (PARAM_ThingMagicProtocolConfiguration)msgGetConfigResp.Custom[0];
                initq = Convert.ToInt32(protocolConfig.Gen2CustomParameters.Gen2Q.InitQValue);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            if (protocolConfig.Gen2CustomParameters.Gen2Q.Gen2QType.Equals(ENUM_QType.Static))
            {
                return new Gen2.StaticQ((byte)initq);
            }
            else
            {
                return new Gen2.DynamicQ();
            }
        }

        private object SetGen2Q(Object val)
        {
            if (val is Gen2.StaticQ)
            {
                Gen2.StaticQ q = ((Gen2.StaticQ)val);
                if (q.InitialQ < 0 || q.InitialQ > 15)
                {
                    throw new ReaderException("Value of /reader/gen2/q is out of range. Should be between 0 and 15");
                }
                SetCustomGen2Q(val);
            }
            else
            {
                SetCustomGen2Q(val);
            }
            return val;
        }

        /// <summary>
        /// Set Gen2Q
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetCustomGen2Q(Object val)
        {
            PARAM_Gen2Q q = new PARAM_Gen2Q();
            if (val is Gen2.StaticQ)
            {
                q.Gen2QType = ENUM_QType.Static;
                q.InitQValue = ((Gen2.StaticQ)val).InitialQ;
            }
            else
            {
                q.Gen2QType = ENUM_QType.Dynamic;
                q.InitQValue = 0;
            }
            PARAM_Gen2CustomParameters custGen2 = new PARAM_Gen2CustomParameters();
            custGen2.Gen2Q = q;
            PARAM_ThingMagicProtocolConfiguration protocolConfig = new PARAM_ThingMagicProtocolConfiguration();
            protocolConfig.Gen2CustomParameters = custGen2;
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.AddCustomParameter(protocolConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Gen2InitQ
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetGen2InitQ(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;

            Gen2.InitQ gen2InitQ = (Gen2.InitQ)val;

            PARAM_InitQ initQ = new PARAM_InitQ();
            initQ.qEnable = gen2InitQ.qEnable;

            PARAM_qValue qVal = new PARAM_qValue();
            if (initQ.qEnable == true)
            {
                if (gen2InitQ.initialQ < 2 || gen2InitQ.initialQ > 10)
                {
                    throw new ReaderException("Value of custom Gen2 InitQ should be between 2 and 10");
                }
                qVal.value = Convert.ToByte(gen2InitQ.initialQ);
                initQ.qValue = qVal;
            }

            PARAM_Gen2CustomParameters custGen2 = new PARAM_Gen2CustomParameters();
            custGen2.InitQ = (PARAM_InitQ)initQ;

            PARAM_ThingMagicProtocolConfiguration protocolConfig = new PARAM_ThingMagicProtocolConfiguration();
            protocolConfig.Gen2CustomParameters = (PARAM_Gen2CustomParameters)custGen2;

            msgSetConfig.AddCustomParameter(protocolConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Gen2InitQ
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetGen2InitQ(Object val)
        {
            PARAM_ThingMagicProtocolConfiguration protocolConfig;
            PARAM_InitQ initQ = new PARAM_InitQ();
            PARAM_qValue qVal = new PARAM_qValue();
            Gen2.InitQ q = new Gen2.InitQ();

            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicProtocolConfiguration);
                protocolConfig = (PARAM_ThingMagicProtocolConfiguration)msgGetConfigResp.Custom[0];
                initQ = (PARAM_InitQ)(protocolConfig.Gen2CustomParameters.InitQ);
                q.qEnable = initQ.qEnable;
                qVal.value = Convert.ToByte(initQ.qValue.value);
                q.initialQ = qVal.value;
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return q;
        }

        /// <summary>
        /// Set Gen2SendSelect
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetGen2SendSelect(Object val)
        {
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.ResetToFactoryDefault = false;

            PARAM_sendSelect sendSel = new PARAM_sendSelect();
            sendSel.selectValue = (bool)Convert.ToBoolean(val);

            PARAM_Gen2CustomParameters custGen2 = new PARAM_Gen2CustomParameters();
            custGen2.sendSelect = (PARAM_sendSelect)sendSel;

            PARAM_ThingMagicProtocolConfiguration protocolConfig = new PARAM_ThingMagicProtocolConfiguration();
            protocolConfig.Gen2CustomParameters = (PARAM_Gen2CustomParameters)custGen2;

            msgSetConfig.AddCustomParameter(protocolConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Gen2SendSelect
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetGen2SendSelect(Object val)
        {
            bool sendSelectVal = false;
            PARAM_ThingMagicProtocolConfiguration protocolConfig;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicProtocolConfiguration);
                protocolConfig = (PARAM_ThingMagicProtocolConfiguration)msgGetConfigResp.Custom[0];
                sendSelectVal = (bool)Convert.ToBoolean(protocolConfig.Gen2CustomParameters.sendSelect.selectValue);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }

            return sendSelectVal;
        }

        /// <summary>
        /// Set write transmit power list
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetWriteTransmitPowerList(Object val)
        {
            int power = 0;
            int[] amode = new int[antennaMax];
            string[] rpoint = new string[antennaMax];
            PARAM_ThingMagicAntennaConfiguration[] paramAntennaConfig = new PARAM_ThingMagicAntennaConfiguration[antennaMax];
            //Get readpointdescription, antennamode
            MSG_GET_READER_CONFIG msgGetConfig = new MSG_GET_READER_CONFIG();
            msgGetConfig.RequestedData = ENUM_GetReaderConfigRequestedData.Identification;
            PARAM_ThingMagicDeviceControlConfiguration deviceConfig = new PARAM_ThingMagicDeviceControlConfiguration();
            deviceConfig.RequestedData = ENUM_ThingMagicControlConfiguration.ThingMagicAntennaConfiguration;
            msgGetConfig.AddCustomParameter(deviceConfig);
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgGetConfig);
                for (int count = 0; count < antennaMax; count++)
                {
                    paramAntennaConfig[count] = (PARAM_ThingMagicAntennaConfiguration)msgGetConfigResp.Custom[count];
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            //Set PortWritePowerList
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            int[][] prpListValues = (int[][])val;
            for (int i = 0; i < prpListValues.Length; i++)
            {
                int[] row = prpListValues[i];
                PARAM_ThingMagicAntennaConfiguration pa = new PARAM_ThingMagicAntennaConfiguration();
                PARAM_RFTransmitter prf = new PARAM_RFTransmitter();
                List<int> pwrValueList = new List<int>();
                foreach (DictionaryEntry pwrEntry in PowerIndexTable)
                {
                    pwrValueList.Add(Convert.ToInt32(pwrEntry.Key));
                }
                int[] pwrValue = pwrValueList.ToArray();
                Array.Sort(pwrValue);
                if ((row[0] > 0))
                {
                    power = row[1];
                    ValidatePowerLevel(power);
                    if ((model.Equals("Astra-EX") || model.Equals("Astra200")) && (regionId.Equals(Region.NA)) && (row[0] == 1))
                    {
                        if (power > 3000)
                        {
                            throw new ArgumentOutOfRangeException(String.Format("Requested power ({0:D}) too high (RFPowerMax={1:D}cdBm)", power, 3000));
                        }
                    }
                    if (model.Equals("Astra200") && (regionId.Equals(Region.EU)) && (row[0] == 1))
                    {
                        if (power > 2900)
                        {
                            throw new ArgumentOutOfRangeException(String.Format("Requested power ({0:D}) too high (RFPowerMax={1:D}cdBm)", power, 2900));
                        }
                    }
                    if (true == PowerIndexTable.ContainsKey(Convert.ToInt16(power)))
                    {
                        paramAntennaConfig[i].WriteTransmitPower.WriteTransmitPower = Convert.ToUInt16(PowerIndexTable[Convert.ToInt16(power)]);
                    }
                    else
                    {
                        power = RoundOffPowerLevel(power, pwrValue);
                        paramAntennaConfig[i].WriteTransmitPower.WriteTransmitPower = Convert.ToUInt16(PowerIndexTable[Convert.ToInt16(power)]);
                    }
                    msgSetConfig.AddCustomParameter(paramAntennaConfig[i]);
                }
                else
                {
                    throw new ArgumentOutOfRangeException("Antenna id is invalid");
                }
            }
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Set Gen2Target
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetCustomGen2Target(Object val)
        {
            PARAM_ThingMagicTargetStrategy target = new PARAM_ThingMagicTargetStrategy();
            target.ThingMagicTargetStrategyValue = (ENUM_ThingMagicC1G2TargetStrategy)val;
            PARAM_Gen2CustomParameters custGen2 = new PARAM_Gen2CustomParameters();
            custGen2.ThingMagicTargetStrategy = target;
            PARAM_ThingMagicProtocolConfiguration protocolConfig = new PARAM_ThingMagicProtocolConfiguration();
            protocolConfig.Gen2CustomParameters = custGen2;
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.AddCustomParameter(protocolConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get Gen2Target
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object GetCustomGen2Target(Object val)
        {
            //Get Gen2Target
            int target = 0;
            PARAM_ThingMagicProtocolConfiguration protocolConfig;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicProtocolConfiguration);
                protocolConfig = (PARAM_ThingMagicProtocolConfiguration)msgGetConfigResp.Custom[0];
                target = Convert.ToInt32(protocolConfig.Gen2CustomParameters.ThingMagicTargetStrategy.ThingMagicTargetStrategyValue);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return (Gen2.Target)target;
        }

        /// <summary>
        /// Set Session
        /// </summary>
        /// <param name="val"></param>
        /// <returns>object</returns>
        private object SetSession(Object val)
        {
            PARAM_C1G2SingulationControl singulation = new PARAM_C1G2SingulationControl();
            List<PARAM_AntennaConfiguration> antConfigList = new List<PARAM_AntennaConfiguration>();
            foreach (int antennaId in antennaPorts)
            {
                if ((Convert.ToUInt16(val)) > 3)
                {
                    throw new Exception("Invalid argument");
                }
                singulation.Session = new LTKD.TwoBits(Convert.ToUInt16(val));
                PARAM_AntennaConfiguration antConfig = new PARAM_AntennaConfiguration();
                UNION_AirProtocolInventoryCommandSettings airProtocolInventoryList = new UNION_AirProtocolInventoryCommandSettings();
                PARAM_C1G2InventoryCommand inventoryCommand = new PARAM_C1G2InventoryCommand();
                inventoryCommand.C1G2SingulationControl = singulation;
                airProtocolInventoryList.Add(inventoryCommand);
                antConfig.AirProtocolInventoryCommandSettings = airProtocolInventoryList;
                //Set the default AntennaId value
                antConfig.AntennaID = (ushort)(antennaId);
                antConfigList.Add(antConfig);
            }
            MSG_SET_READER_CONFIG setReadConfig = new MSG_SET_READER_CONFIG();
            setReadConfig.AntennaConfiguration = antConfigList.ToArray();
            try
            {
                MSG_SET_READER_CONFIG_RESPONSE response = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(setReadConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        private void ValidatePowerLevel(int power)
        {
            if (power < rfPowerMin)
            {
                throw new ArgumentOutOfRangeException(String.Format("Requested power ({0:D}) too low (RFPowerMin={1:D}cdBm)", power, rfPowerMin));
            }
            if (power > rfPowerMax)
            {
                throw new ArgumentOutOfRangeException(String.Format("Requested power ({0:D}) too high (RFPowerMax={1:D}cdBm)", power, rfPowerMax));
            }
        }

        private int RoundOffPowerLevel(int setPower, int[] pwrValue)
        {
            for (int pwrIndex = 0; pwrIndex < pwrValue.Length; pwrIndex++)
            {
                if ((setPower > pwrValue[pwrIndex]) && (setPower < pwrValue[pwrIndex + 1]))
                {
                    setPower = pwrValue[pwrIndex];
                    break;
                }
            }
            return setPower;
        }

        /// <summary>
        /// Get ISO18K6BProtocolConfigurationParams
        /// </summary>
        /// <param name="val">enum</param>
        /// <returns>int</returns>
        private int GetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams val)
        {
            // Get ISO18K6BProtocolConfigurationParams
            int protocolConfigValue = 0;
            PARAM_ThingMagicProtocolConfiguration protocolConfig;
            try
            {
                MSG_GET_READER_CONFIG_RESPONSE msgGetConfigResp = GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration.ThingMagicProtocolConfiguration);
                protocolConfig = (PARAM_ThingMagicProtocolConfiguration)msgGetConfigResp.Custom[0];
                // Parse response to get iso18k6b protocol configuration params
                switch (val)
                {
                    case ISO18K6BProtocolConfigurationParams.Delimiter:
                        protocolConfigValue = Convert.ToInt32(protocolConfig.ISO18K6BCustomParameters.ThingMagicISO180006BDelimiter.ISO18K6BDelimiter);
                        break;
                    case ISO18K6BProtocolConfigurationParams.ModulationDepth:
                        protocolConfigValue = Convert.ToInt32(protocolConfig.ISO18K6BCustomParameters.ThingMagicISO18K6BModulationDepth.ISO18K6BModulationDepth);
                        break;
                    case ISO18K6BProtocolConfigurationParams.LinkFrequency:
                        protocolConfigValue = Convert.ToInt32(protocolConfig.ISO18K6BCustomParameters.ThingMagicISO18K6BLinkFrequency.ISO18K6BLinkFrequency);
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return protocolConfigValue;
        }

        /// <summary>
        /// Set ISO18K6BProtocolConfigurationParams
        /// </summary>
        /// <param name="val"></param>
        /// <param name="type">enum</param>
        /// <returns>object</returns>
        private object SetCustomISO18K6BProtocolConfigurationParams(ISO18K6BProtocolConfigurationParams type, Object val)
        {
            PARAM_ISO18K6BCustomParameters custIso18k6b = new PARAM_ISO18K6BCustomParameters();
            //Build iso18k6b protocol configuration params
            switch (type)
            {
                case ISO18K6BProtocolConfigurationParams.Delimiter:
                    PARAM_ThingMagicISO180006BDelimiter delimiter = new PARAM_ThingMagicISO180006BDelimiter();
                    delimiter.ISO18K6BDelimiter = (ENUM_ThingMagicCustom18K6BDelimiter)val;
                    custIso18k6b.ThingMagicISO180006BDelimiter = delimiter;
                    break;
                case ISO18K6BProtocolConfigurationParams.ModulationDepth:
                    PARAM_ThingMagicISO18K6BModulationDepth modulationDepth = new PARAM_ThingMagicISO18K6BModulationDepth();
                    modulationDepth.ISO18K6BModulationDepth = (ENUM_ThingMagicCustom18K6BModulationDepth)val;
                    custIso18k6b.ThingMagicISO18K6BModulationDepth = modulationDepth;
                    break;
                case ISO18K6BProtocolConfigurationParams.LinkFrequency:
                    PARAM_ThingMagicISO18K6BLinkFrequency linkFrequency = new PARAM_ThingMagicISO18K6BLinkFrequency();
                    linkFrequency.ISO18K6BLinkFrequency = (ENUM_ThingMagicCustom18K6BLinkFrequency)val;
                    custIso18k6b.ThingMagicISO18K6BLinkFrequency = linkFrequency;
                    break;
            }
            PARAM_ThingMagicProtocolConfiguration protocolConfig = new PARAM_ThingMagicProtocolConfiguration();
            protocolConfig.ISO18K6BCustomParameters = custIso18k6b;
            MSG_SET_READER_CONFIG msgSetConfig = new MSG_SET_READER_CONFIG();
            MSG_SET_READER_CONFIG_RESPONSE msgSetConfigResp;
            msgSetConfig.AddCustomParameter(protocolConfig);
            try
            {
                msgSetConfigResp = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(msgSetConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return val;
        }

        /// <summary>
        /// Get reader configuration response.
        /// </summary>
        /// <param name="requestData">ENUM_GetReaderConfigRequestedData</param>
        private MSG_GET_READER_CONFIG_RESPONSE GetReaderConfigResponse(ENUM_GetReaderConfigRequestedData requestData)
        {
            MSG_GET_READER_CONFIG getReadConfig = new MSG_GET_READER_CONFIG();
            MSG_GET_READER_CONFIG_RESPONSE getReadConfigResp;
            getReadConfig.RequestedData = requestData;
            try
            {
                getReadConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(getReadConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return getReadConfigResp;
        }

        /// <summary>
        /// Get custom reader configuration response.
        /// </summary>
        /// <param name="requestData">ENUM_ThingMagicControlConfiguration</param>
        private MSG_GET_READER_CONFIG_RESPONSE GetCustomReaderConfigResponse(ENUM_ThingMagicControlConfiguration requestData)
        {
            // Create Get reader config message
            MSG_GET_READER_CONFIG getReadConfig = new MSG_GET_READER_CONFIG();
            MSG_GET_READER_CONFIG_RESPONSE getReadConfigResp;
            getReadConfig.RequestedData = ENUM_GetReaderConfigRequestedData.Identification;
            PARAM_ThingMagicDeviceControlConfiguration controlConfig = new PARAM_ThingMagicDeviceControlConfiguration();
            // Set the requested data (i.e., controlconfig object)
            controlConfig.RequestedData = requestData;
            //add to GET_READER_CONFIG message.
            getReadConfig.AddCustomParameter(controlConfig);
            //now the message is fully framed send the message
            try
            {
                getReadConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(getReadConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return getReadConfigResp;
        }

        /// <summary>
        /// Get Custom Reader Capabilities Response
        /// </summary>
        /// <param name="requestData">ENUM_GetReaderCapabilitiesRequestedData</param>
        private MSG_GET_READER_CAPABILITIES_RESPONSE GetCustomReaderCapabilitiesResponse(ENUM_ThingMagicControlCapabilities requestData)
        {
            //Initialize GET_READER_CAPABILITIES message
            MSG_GET_READER_CAPABILITIES msgGetCapabilities = new MSG_GET_READER_CAPABILITIES();
            MSG_GET_READER_CAPABILITIES_RESPONSE msgGetCapabilitiesResp;
            msgGetCapabilities.RequestedData = ENUM_GetReaderCapabilitiesRequestedData.General_Device_Capabilities;
            PARAM_ThingMagicDeviceControlCapabilities deviceCapabilities = new PARAM_ThingMagicDeviceControlCapabilities();
            //Set the requested data
            deviceCapabilities.RequestedData = requestData;
            // And add to GET_READER_CAPABILITIES message.
            msgGetCapabilities.AddCustomParameter(deviceCapabilities);
            //now the message is fully framed send the message
            try
            {
                msgGetCapabilitiesResp = (MSG_GET_READER_CAPABILITIES_RESPONSE)SendLlrpMessage(msgGetCapabilities);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return msgGetCapabilitiesResp;
        }

        #region SetRFControlMode
        /// <summary>
        /// Set RF Control
        /// </summary>
        /// <param name="rfControl"></param>
        private void SetRFControl(PARAM_C1G2RFControl rfControl)
        {
            MSG_SET_READER_CONFIG setConfig = new MSG_SET_READER_CONFIG();
            List<PARAM_AntennaConfiguration> antCfglist = new List<PARAM_AntennaConfiguration>();
            PARAM_AntennaConfiguration antCfg = new PARAM_AntennaConfiguration();
            UNION_AirProtocolInventoryCommandSettings airProtocolList = new UNION_AirProtocolInventoryCommandSettings();
            PARAM_C1G2InventoryCommand airProtocol = new PARAM_C1G2InventoryCommand();
            airProtocol.C1G2RFControl = rfControl;
            airProtocolList.Add(airProtocol);
            antCfg.AirProtocolInventoryCommandSettings = airProtocolList;
            antCfg.AntennaID = (ushort)0;
            antCfglist.Add(antCfg);
            setConfig.AntennaConfiguration = antCfglist.ToArray();
            try
            {
                MSG_SET_READER_CONFIG_RESPONSE setResponse = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(setConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }
        #endregion SetRFControlMode

        #region GetLinkFrequency
        /// <summary>
        /// Get Link Frequency
        /// </summary>
        /// <param name="frequency"></param>
        /// <returns>Gen2.LinkFrequency</returns>
        private Gen2.LinkFrequency GetLinkFrequency(Object frequency)
        {
            int freq = int.Parse(frequency.ToString());
            switch (freq)
            {
                case 250000:
                    return Gen2.LinkFrequency.LINK250KHZ;
                case 320000:
                    return Gen2.LinkFrequency.LINK320KHZ;
                case 640000:
                    return Gen2.LinkFrequency.LINK640KHZ;
                default:
                    throw new ArgumentException("Unsupported tag BLF.");
            }
        }
        #endregion GetLinkFrequency

        #region GetLinkFrequencyToInt

        private static int GetLinkFrequencyToInt(Object val)
        {
            switch ((Gen2.LinkFrequency)val)
            {
                case Gen2.LinkFrequency.LINK250KHZ:
                    {
                        return 250000;
                    }
                case Gen2.LinkFrequency.LINK320KHZ:
                    {
                        return 320000;
                    }
                case Gen2.LinkFrequency.LINK640KHZ:
                    {
                        return 640000;
                    }
                default:
                    {
                        throw new ArgumentException("Unsupported tag BLF.");
                    }
            }
        }
        #endregion

        #region Gen2BLFObjectToInt

        private static int Gen2BLFObjectToInt(Object val)
        {
            switch ((Gen2.LinkFrequency)val)
            {
                case Gen2.LinkFrequency.LINK250KHZ:
                    {
                        return 0;
                    }
                case Gen2.LinkFrequency.LINK320KHZ:
                    {
                        return 2;
                    }
                case Gen2.LinkFrequency.LINK640KHZ:
                    {
                        return 4;
                    }
                default:
                    {
                        throw new ArgumentException("Unsupported tag BLF.");
                    }
            }
        }
        #endregion

        #region CacheCapabilities

        private void CacheRFModeTable(PARAM_UHFBandCapabilities capabilities)
        {
            RFModeCache = new Hashtable();
            UNION_AirProtocolUHFRFModeTable rfModeList;
            rfModeList = capabilities.AirProtocolUHFRFModeTable;
            PARAM_C1G2UHFRFModeTableEntry[] gen2RFList = ((PARAM_C1G2UHFRFModeTable)rfModeList[0]).C1G2UHFRFModeTableEntry;
            foreach (PARAM_C1G2UHFRFModeTableEntry rfMode in gen2RFList)
            {
                RFModeCache.Add(rfMode.ModeIdentifier, rfMode);
            }
        }
        #endregion CacheCapabilities

        #region GetRFcontrol

        private PARAM_C1G2RFControl GetRFcontrol()
        {
            PARAM_C1G2RFControl rfControl = null;
            MSG_GET_READER_CONFIG readerConfig = new MSG_GET_READER_CONFIG();
            readerConfig.RequestedData = ENUM_GetReaderConfigRequestedData.AntennaConfiguration;
            MSG_GET_READER_CONFIG_RESPONSE readerConfigResp = (MSG_GET_READER_CONFIG_RESPONSE)SendLlrpMessage(readerConfig);
            // get active mode index
            // Fetch the antenna configurationList from the response
            PARAM_AntennaConfiguration[] antConfigList = readerConfigResp.AntennaConfiguration;

            for (int getAntCfg = 0; getAntCfg < antConfigList.Length; getAntCfg++)
            {
                UNION_AirProtocolInventoryCommandSettings getProtocolList = antConfigList[getAntCfg].AirProtocolInventoryCommandSettings;
                rfControl = ((PARAM_C1G2InventoryCommand)getProtocolList[0]).C1G2RFControl;
            }
            return rfControl;
        }
        #endregion GetRFcontrol

        /// <summary>
        /// Set Tari
        /// </summary>
        /// <param name="tari"></param>
        /// <returns>object</returns>
        private object SetTari(Object tari)
        {
            PARAM_C1G2RFControl rfControl = GetRFcontrol();
            uint modeIndex = rfControl.ModeIndex;
            PARAM_C1G2UHFRFModeTableEntry RFMode = (PARAM_C1G2UHFRFModeTableEntry)RFModeCache[modeIndex];
            uint maxTari = RFMode.MaxTariValue;
            uint minTari = RFMode.MinTariValue;
            rfControl.Tari = Convert.ToUInt16(GetTariValue((Gen2.Tari)tari));
            if (rfControl.Tari < minTari || rfControl.Tari > maxTari)
            {
                throw new ReaderException("Tari value is out of range for this RF mode");
            }
            //Get the AntennaId value
            List<PARAM_AntennaConfiguration> antConfigList = new List<PARAM_AntennaConfiguration>();
            //Get the TagInventoryStateAware value
            //foreach (int antennaId in antennaPorts)
            {
                PARAM_AntennaConfiguration antConfig = new PARAM_AntennaConfiguration();
                //Get the ModeIndex value
                UNION_AirProtocolInventoryCommandSettings airProtocolInventoryList = new UNION_AirProtocolInventoryCommandSettings();

                PARAM_C1G2InventoryCommand inventoryCommand = new PARAM_C1G2InventoryCommand();

                inventoryCommand.C1G2RFControl = rfControl;
                //Set the default TagInventoryStateAware value
                inventoryCommand.TagInventoryStateAware = false;
                airProtocolInventoryList.Add(inventoryCommand);

                antConfig.AirProtocolInventoryCommandSettings = airProtocolInventoryList;
                //Set the default AntennaId value
                antConfig.AntennaID = 0;// (ushort)(antennaId);
                antConfigList.Add(antConfig);
            }
            MSG_SET_READER_CONFIG setReadConfig = new MSG_SET_READER_CONFIG();
            setReadConfig.AntennaConfiguration = antConfigList.ToArray();
            try
            {
                MSG_SET_READER_CONFIG_RESPONSE response = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(setReadConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            return tari;
        }

        private void EnableReaderNotification()
        {
            List<PARAM_EventNotificationState> eStateList = new List<PARAM_EventNotificationState>();

            PARAM_EventNotificationState aiEvent = new PARAM_EventNotificationState();
            aiEvent.EventType = ENUM_NotificationEventType.AISpec_Event;
            aiEvent.NotificationState = true;
            eStateList.Add(aiEvent);
            PARAM_EventNotificationState roEvent = new PARAM_EventNotificationState();
            roEvent.EventType = ENUM_NotificationEventType.ROSpec_Event;
            roEvent.NotificationState = true;
            eStateList.Add(roEvent);

            MSG_SET_READER_CONFIG setReaderConfig = new MSG_SET_READER_CONFIG();
            setReaderConfig.ReaderEventNotificationSpec = new PARAM_ReaderEventNotificationSpec();
            setReaderConfig.ReaderEventNotificationSpec.EventNotificationState = eStateList.ToArray();

            try
            {
                MSG_SET_READER_CONFIG_RESPONSE setReaderConfigResponse = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(setReaderConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
        }

        private void SetKeepAlive()
        {
            PARAM_KeepaliveSpec kSpec = new PARAM_KeepaliveSpec();
            kSpec.KeepaliveTriggerType = ENUM_KeepaliveTriggerType.Periodic;
            kSpec.PeriodicTriggerValue = (uint)keepAliveTrigger;

            MSG_SET_READER_CONFIG readerConfig = new MSG_SET_READER_CONFIG();
            readerConfig.KeepaliveSpec = kSpec;

            llrp.OnKeepAlive += new delegateKeepAlive(OnKeepAliveReceived);
            try
            {
                MSG_SET_READER_CONFIG_RESPONSE configResponse = (MSG_SET_READER_CONFIG_RESPONSE)SendLlrpMessage(readerConfig);
            }
            catch (Exception ex)
            {
                throw new ReaderException(ex.Message);
            }
            finally
            {
                kSpec = null;
                readerConfig = null;
            }
        }

        void OnKeepAliveReceived(MSG_KEEPALIVE msg)
        {
            keepAliveTime = DateTime.Now;
            msgStartTime = DateTime.Now;
            BuildTransport(false, msg);
            SendLlrpMessage(new MSG_KEEPALIVE_ACK());
            if (continuousReading)
            {
                bool tmp;
                if (isRoAccessReportsComing)
                {
                    lock (isRoAccessReportsComingLock)
                    {
                        tmp = isRoAccessReportsComing;
                        isRoAccessReportsComing = false;
                    }
                }
            }
        }
#if !WindowsCE
        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            TimeSpan diff = (DateTime.Now - keepAliveTime); // time difference in seconds
            if (diff.TotalMilliseconds > (keepAliveTrigger * 4))  // 4 keep alives lost
            {
                //Release the TagQueueEmptyEvent when connection lost
                TagQueueEmptyEvent.Set();
                ReadExceptionPublisher expub = new ReadExceptionPublisher(this, new ReaderException("Connection Lost"));
                Thread trd = new Thread(expub.OnReadException);
                trd.Name = "OnReadException";
                trd.Start();
            }
        }
#endif
        #endregion Private Methods
        /// <summary>
        /// Delete all AccessSpecs from the reader
        /// </summary>
        private void DeleteAccessSpecs()
        {
            MSG_DELETE_ACCESSSPEC_RESPONSE response;

            MSG_DELETE_ACCESSSPEC delAcessSpec = new MSG_DELETE_ACCESSSPEC();
            // Use zero as the ROSpec ID, This means delete all AccessSpecs.
            delAcessSpec.AccessSpecID = 0;
            response = (MSG_DELETE_ACCESSSPEC_RESPONSE)SendLlrpMessage(delAcessSpec);
        }

        private LTKD.Parameter BuildOpSpec(SimpleReadPlan srp)
        {
            TagOp tagOp = srp.Op;
            if ((tagOp is Gen2.ReadData) && typeof(Gen2.SecureReadData) != tagOp.GetType())
            {
                return BuildReadOpSpec((Gen2.ReadData)tagOp);
            }
            if (tagOp is Gen2.WriteData)
            {
                return BuildWriteDataOpSpec(tagOp);
            }
            if (tagOp is Gen2.WriteTag)
            {
                return BuildWriteTagOpSpec(tagOp);
            }
            if (tagOp is Gen2.BlockWrite)
            {
                return BuildBlockWriteTagOpSpec((Gen2.BlockWrite)tagOp);
            }
            if (tagOp is Gen2.Lock)
            {
                return BuildLockTagOpSpec((Gen2.Lock)tagOp);
            }
            if (tagOp is Gen2.Kill)
            {
                return BuildKillTagOpSpec((Gen2.Kill)tagOp);
            }
            if (tagOp is Gen2.BlockPermaLock)
            {
                return BuildBlockPermaLockTagOpSpec((Gen2.BlockPermaLock)tagOp);
            }
            if (tagOp is Gen2.BlockErase)
            {
                return BuildBlockEraseOpSpec((Gen2.BlockErase)tagOp);
            }
            if (tagOp is Gen2.Alien.Higgs2.PartialLoadImage)
            {
                return BuildHiggs2PartialLoadImage((Gen2.Alien.Higgs2.PartialLoadImage)tagOp);
            }
            if (tagOp is Gen2.Alien.Higgs2.FullLoadImage)
            {
                return BuildHiggs2FullLoadImage((Gen2.Alien.Higgs2.FullLoadImage)tagOp);
            }
            if (tagOp is Gen2.Alien.Higgs3.FastLoadImage)
            {
                return BuildHiggs3FastLoadImage((Gen2.Alien.Higgs3.FastLoadImage)tagOp);
            }
            if (tagOp is Gen2.Alien.Higgs3.LoadImage)
            {
                return BuildHiggs3LoadImage((Gen2.Alien.Higgs3.LoadImage)tagOp);
            }
            if (tagOp is Gen2.Alien.Higgs3.BlockReadLock)
            {
                return BuildHiggs3BlockReadLock((Gen2.Alien.Higgs3.BlockReadLock)tagOp);
            }
            if (tagOp is Gen2.NxpGen2TagOp.SetReadProtect)
            {
                return BuildNxpGen2SetReadProtect((Gen2.NxpGen2TagOp.SetReadProtect)tagOp);
            }
            if (tagOp is Gen2.NxpGen2TagOp.ResetReadProtect)
            {
                return BuildNxpGen2ResetReadProtect((Gen2.NxpGen2TagOp.ResetReadProtect)tagOp);
            }
            if (tagOp is Gen2.NxpGen2TagOp.ChangeEas)
            {
                return BuildNxpGen2ChangeEAS((Gen2.NxpGen2TagOp.ChangeEas)tagOp);
            }
            if (tagOp is Gen2.NxpGen2TagOp.Calibrate)
            {
                return BuildNxpGen2Calibrate((Gen2.NxpGen2TagOp.Calibrate)tagOp);
            }
            if (tagOp is Gen2.NXP.G2I.ChangeConfig)
            {
                return BuildNxpG2IChangeConfig((Gen2.NXP.G2I.ChangeConfig)tagOp);
            }
            if (tagOp is Gen2.Impinj.Monza4.QTReadWrite)
            {
                return BuildMonza4QTReadWrite((Gen2.Impinj.Monza4.QTReadWrite)tagOp);
            }
            if (tagOp is Gen2.NxpGen2TagOp.EasAlarm)
            {
                return BuildNxpGen2EASAlarm((Gen2.NxpGen2TagOp.EasAlarm)tagOp);
            }
            if (tagOp is Iso180006b.ReadData)
            {
                return BuildIso180006bReadDataOpSpec((Iso180006b.ReadData)tagOp);
            }
            if (tagOp is Iso180006b.WriteData)
            {
                return BuildIso180006bWriteTagDataOpSpec((Iso180006b.WriteData)tagOp);
            }
            if (tagOp is Iso180006b.LockTag)
            {
                return BuildIso180006bLockTagOpSpec((Iso180006b.LockTag)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.SetCalibrationData)
            {
                return BuildIDsSL900aSetCalibrationDataTagOpSpec((Gen2.IDS.SL900A.SetCalibrationData)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.EndLog)
            {
                return BuildIDsSL900aEndLogTagOpSpec((Gen2.IDS.SL900A.EndLog)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.Initialize)
            {
                return BuildIDsSL900aInitializeTagOpSpec((Gen2.IDS.SL900A.Initialize)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.SetLogMode)
            {
                return BuildIDsSL900aSetLogModeTagOpSpec((Gen2.IDS.SL900A.SetLogMode)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.StartLog)
            {
                return BuildIDsSL900aStartLogTagOpSpec((Gen2.IDS.SL900A.StartLog)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.SetSfeParameters)
            {
                return BuildIDsSL900aSetSFEParamsTagOpSpec((Gen2.IDS.SL900A.SetSfeParameters)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.SetLogLimit)
            {
                return BuildIDsSL900aSetLogLimitsTagOpSpec((Gen2.IDS.SL900A.SetLogLimit)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.SetPassword)
            {
                return BuildIDsSL900aSetIDSPasswordTagOpSpec((Gen2.IDS.SL900A.SetPassword)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.GetSensorValue)
            {
                return BuildIDsSL900aSensorValueTagOpSpec((Gen2.IDS.SL900A.GetSensorValue)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.GetBatteryLevel)
            {
                return BuildIDsSL900aBatteryLevelTagOpSpec((Gen2.IDS.SL900A.GetBatteryLevel)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.GetCalibrationData)
            {
                return BuildIDsSL900aGetCalibrationDataTagOpSpec((Gen2.IDS.SL900A.GetCalibrationData)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.GetLogState)
            {
                return BuildIDsSL900aLoggingFormTagOpSpec((Gen2.IDS.SL900A.GetLogState)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.GetMeasurementSetup)
            {
                return BuildIDSsL900aGetMeasurementSetupTagOpSpec((Gen2.IDS.SL900A.GetMeasurementSetup)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.AccessFifo)
            {
                return BuildIDsSL900aAccessFifoTagOpSpec((Gen2.IDS.SL900A.AccessFifo)tagOp);
            }
            else if (tagOp is Gen2.IDS.SL900A.SetShelfLife)
            {
                return BuildIDsSL900aSetShelfLifeTagOpSpec((Gen2.IDS.SL900A.SetShelfLife)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.ActivateSecureMode)
            {
                return BuildIAVActivateSecureMode((Gen2.Denatran.IAV.ActivateSecureMode)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.ActivateSiniavMode)
            {
                return BuildIAVActivateSiniavMode((Gen2.Denatran.IAV.ActivateSiniavMode)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.AuthenticateOBU)
            {
                return BuildIAVAuthenticateOBU((Gen2.Denatran.IAV.AuthenticateOBU)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.OBUAuthFullPass1)
            {
                return BuildIAVOBUAuthenticateFullPass1((Gen2.Denatran.IAV.OBUAuthFullPass1)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.OBUAuthFullPass2)
            {
                return BuildIAVOBUAuthenticateFullPass2((Gen2.Denatran.IAV.OBUAuthFullPass2)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.OBUAuthID)
            {
                return BuildIAVOBUAuthenticateID((Gen2.Denatran.IAV.OBUAuthID)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.OBUReadFromMemMap)
            {
                return BuildIAVOBUReadFromMemMap((Gen2.Denatran.IAV.OBUReadFromMemMap)tagOp);
            }
            else if (tagOp is Gen2.Denatran.IAV.OBUWriteToMemMap)
            {
                return BuildIAVOBUWriteToMemMap((Gen2.Denatran.IAV.OBUWriteToMemMap)tagOp);
            }
            else if (tagOp is Gen2.NXP.AES.Authenticate)
            {
                return BuildNXPAuthenticationTagOpSpec((Gen2.NXP.AES.Authenticate)tagOp);
            }
            else if (tagOp is Gen2.NXP.AES.ReadBuffer)
            {
                return BuildNXPAESReadBuffer((Gen2.NXP.AES.ReadBuffer)tagOp);
            }
            else if (tagOp is Gen2.NXP.AES.Untraceable)
            {
                return BuildNXPAESUntraceable((Gen2.NXP.AES.Untraceable)tagOp);
            }
            else
            {
                throw new FeatureNotSupportedException("Tag Operation not supported");
            }
        }

        private void BuildtagOpListSpec(SimpleReadPlan srp, PARAM_AccessCommand accessCommand)
        {
            TagOp tagOp = srp.Op;
            TagOpList tagOpList = (TagOpList)tagOp;
            PARAM_AccessCommand command = accessCommand;
            if (tagOpList.list.Count == 1)
            {
                srp.Op = (TagOp)tagOpList.list[0];
                accessCommand.AccessCommandOpSpec.Add(BuildOpSpec(srp));

            }
            else if (tagOpList.list.Count == 2)
            {
                if ((tagOpList.list[0] is Gen2.WriteData) && (tagOpList.list[1] is Gen2.ReadData))
                {
                    Gen2.WriteData writeData = (Gen2.WriteData)tagOpList.list[0];
                    Gen2.ReadData readData = (Gen2.ReadData)tagOpList.list[1];
                    accessCommand.AccessCommandOpSpec.Add(BuildWriteDataOpSpec(writeData));
                    accessCommand.AccessCommandOpSpec.Add(BuildReadOpSpec(readData));


                }
                else if ((tagOpList.list[0] is Gen2.WriteTag) && (tagOpList.list[1] is Gen2.ReadData))
                {
                    Gen2.WriteTag writeTag = (Gen2.WriteTag)tagOpList.list[0];
                    Gen2.ReadData rData = (Gen2.ReadData)tagOpList.list[1];
                    accessCommand.AccessCommandOpSpec.Add(BuildWriteTagOpSpec(writeTag));
                    accessCommand.AccessCommandOpSpec.Add(BuildReadOpSpec(rData));


                }
                else
                {
                    throw new FeatureNotSupportedException("Unsupported operation");
                }
            }
            else
            {
                throw new FeatureNotSupportedException("Unsupported operation");
            }
        }

        /// <summary>
        /// Create a OpSpec that reads from user memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_C1G2Read</returns>
        private PARAM_C1G2Read BuildReadOpSpec(Gen2.ReadData tagOperation)
        {
            PARAM_C1G2Read c1g2Read = new PARAM_C1G2Read();

            c1g2Read.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            if ((int)(((Gen2.ReadData)tagOperation).Bank) > 3)
            {
                throw new FeatureNotSupportedException("Operation not supported");
            }

            // Memory Bank
            if (((ushort)tagOperation.Bank) > 3)
            {
                throw new Exception("Invalid argument");
            }
            c1g2Read.MB = new LTKD.TwoBits((ushort)tagOperation.Bank);

            c1g2Read.WordCount = (ushort)tagOperation.Len;
            c1g2Read.WordPointer = (ushort)tagOperation.WordAddress;

            // Set the OpSpecID to a unique number.
            c1g2Read.OpSpecID = ++OpSpecID;
            return c1g2Read;
        }

        /// <summary>
        /// Create a OpSpec that write into Gen2 memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_C1G2Write</returns>
        private PARAM_C1G2Write BuildWriteDataOpSpec(TagOp tagOperation)
        {
            PARAM_C1G2Write c1g2Write = new PARAM_C1G2Write();
            int dataLength = 0;

            c1g2Write.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            // Memory Bank
            if (((ushort)((Gen2.WriteData)tagOperation).Bank) > 3)
            {
                throw new Exception("Invalid argument");
            }
            c1g2Write.MB = new LTKD.TwoBits(((ushort)((Gen2.WriteData)tagOperation).Bank));
            c1g2Write.WordPointer = (ushort)((Gen2.WriteData)tagOperation).WordAddress;
            dataLength = ((Gen2.WriteData)tagOperation).Data.Length;

            //Data to be written
            LTKD.UInt16Array data = new LTKD.UInt16Array();
            List<ushort> dataWrite = new List<ushort>();
            for (int i = 0; i < dataLength; i++)
            {
                dataWrite.Add(((Gen2.WriteData)tagOperation).Data[i]);
            }
            data.data = dataWrite;
            c1g2Write.WriteData = LTKD.UInt16Array.FromString(data.ToString());

            // Set the OpSpecID to a unique number.
            c1g2Write.OpSpecID = ++OpSpecID;
            return c1g2Write;
        }

        /// <summary>
        /// Create a OpSpec that write epc into Gen2 memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_ThingMagicWriteTag</returns>
        private PARAM_ThingMagicWriteTag BuildWriteTagOpSpec(TagOp tagOperation)
        {
            PARAM_ThingMagicWriteTag c1g2WriteTag = new PARAM_ThingMagicWriteTag();

            c1g2WriteTag.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            c1g2WriteTag.WriteData = LTKD.UInt16Array.FromHexString(((Gen2.WriteTag)tagOperation).Epc.EpcString);

            // Set the OpSpecID to a unique number.
            c1g2WriteTag.OpSpecID = ++OpSpecID;
            return c1g2WriteTag;
        }

        /// <summary>
        /// Create a OpSpec that Write a block of data into Gen2 memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_C1G2BlockWrite</returns>
        private PARAM_C1G2BlockWrite BuildBlockWriteTagOpSpec(Gen2.BlockWrite tagOperation)
        {
            PARAM_C1G2BlockWrite c1g2BlockWrite = new PARAM_C1G2BlockWrite();
            int dataLength = 0;

            //AccessPassword
            c1g2BlockWrite.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            //Memory Bank
            if (((ushort)(tagOperation.Bank)) > 3)
            {
                throw new Exception("Invalid argument");
            }
            c1g2BlockWrite.MB = new LTKD.TwoBits((ushort)(tagOperation.Bank));

            //Data to be written
            dataLength = tagOperation.Data.Length;
            LTKD.UInt16Array data = new LTKD.UInt16Array();
            List<ushort> dataWrite = new List<ushort>();
            for (int i = 0; i < dataLength; i++)
            {
                dataWrite.Add(tagOperation.Data[i]);
            }
            data.data = dataWrite;
            c1g2BlockWrite.WriteData = LTKD.UInt16Array.FromString(data.ToString());

            //WordPointer
            c1g2BlockWrite.WordPointer = (ushort)tagOperation.WordPtr;

            // Set the OpSpecID to a unique number.
            c1g2BlockWrite.OpSpecID = ++OpSpecID;

            return c1g2BlockWrite;
        }

        /// <summary>
        /// Create a OpSpec to Lock the Gen2 memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_C1G2Lock</returns>
        private PARAM_C1G2Lock BuildLockTagOpSpec(Gen2.Lock tagOperation)
        {
            PARAM_C1G2Lock c1g2Lock = new PARAM_C1G2Lock();

            //AccessPassword
            c1g2Lock.AccessPassword = tagOperation.AccessPassword;

            //Gen2.LockAction
            Gen2.LockAction lckAction = tagOperation.LockAction;

            //Build C1G2LockPayload
            List<PARAM_C1G2LockPayload> lck = new List<PARAM_C1G2LockPayload>();
            PARAM_C1G2LockPayload payLoad = GetC1G2Payload(lckAction);
            lck.Add(payLoad);
            c1g2Lock.C1G2LockPayload = lck.ToArray();

            // Set the OpSpecID to a unique number.
            c1g2Lock.OpSpecID = ++OpSpecID;

            return c1g2Lock;
        }

        /// <summary>
        /// Build C1G2PayLoad
        /// </summary>
        /// <param name="lckAction"></param>
        /// <returns>PARAM_C1G2LockPayload</returns>
        private PARAM_C1G2LockPayload GetC1G2Payload(Gen2.LockAction lckAction)
        {
            PARAM_C1G2LockPayload payLoad = new PARAM_C1G2LockPayload();

            //EPC Memory
            if (lckAction.ToString().Equals(Gen2.LockAction.EPC_LOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Read_Write;
                payLoad.DataField = ENUM_C1G2LockDataField.EPC_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.EPC_PERMALOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Lock;
                payLoad.DataField = ENUM_C1G2LockDataField.EPC_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.EPC_PERMAUNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.EPC_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.EPC_UNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.EPC_Memory;
            }

            //Reserved Memory
            if (lckAction.ToString().Equals(Gen2.LockAction.ACCESS_LOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Read_Write;
                payLoad.DataField = ENUM_C1G2LockDataField.Access_Password;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.ACCESS_PERMALOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Lock;
                payLoad.DataField = ENUM_C1G2LockDataField.Access_Password;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.ACCESS_PERMAUNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.Access_Password;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.ACCESS_UNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.Access_Password;
            }

            //Reserved Memory
            if (lckAction.ToString().Equals(Gen2.LockAction.KILL_LOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Read_Write;
                payLoad.DataField = ENUM_C1G2LockDataField.Kill_Password;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.KILL_PERMALOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Lock;
                payLoad.DataField = ENUM_C1G2LockDataField.Kill_Password;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.KILL_PERMAUNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.Kill_Password;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.KILL_UNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.Kill_Password;
            }

            //TID Memory
            if (lckAction.ToString().Equals(Gen2.LockAction.TID_LOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Read_Write;
                payLoad.DataField = ENUM_C1G2LockDataField.TID_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.TID_PERMALOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Lock;
                payLoad.DataField = ENUM_C1G2LockDataField.TID_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.TID_PERMAUNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.TID_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.TID_UNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.TID_Memory;
            }

            //User Memory
            if (lckAction.ToString().Equals(Gen2.LockAction.USER_LOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Read_Write;
                payLoad.DataField = ENUM_C1G2LockDataField.User_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.USER_PERMALOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Lock;
                payLoad.DataField = ENUM_C1G2LockDataField.User_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.USER_PERMAUNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Perma_Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.User_Memory;
            }
            else if (lckAction.ToString().Equals(Gen2.LockAction.USER_UNLOCK.ToString()))
            {
                payLoad.Privilege = ENUM_C1G2LockPrivilege.Unlock;
                payLoad.DataField = ENUM_C1G2LockDataField.User_Memory;
            }
            return payLoad;
        }

        /// <summary>
        /// Create a OpSpec that kills the tag
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_C1G2Kill</returns>
        private PARAM_C1G2Kill BuildKillTagOpSpec(Gen2.Kill tagOperation)
        {
            PARAM_C1G2Kill c1g2Kill = new PARAM_C1G2Kill();

            // KillPassword
            c1g2Kill.KillPassword = tagOperation.KillPassword;

            // Set the OpSpecID to a unique number.
            c1g2Kill.OpSpecID = ++OpSpecID;

            return c1g2Kill;
        }

        /// <summary>
        /// Create a OpSpec that blocks permanently Gen2 memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_ThingMagicBlockPermalock</returns>
        private PARAM_ThingMagicBlockPermalock BuildBlockPermaLockTagOpSpec(Gen2.BlockPermaLock tagOperation)
        {
            PARAM_ThingMagicBlockPermalock c1g2BlockPermaLock = new PARAM_ThingMagicBlockPermalock();

            //AccessPassword
            c1g2BlockPermaLock.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            //Memory Bank
            if (((ushort)(tagOperation.Bank)) > 3)
            {
                throw new Exception("Invalid argument");
            }
            c1g2BlockPermaLock.MB = new LTKD.TwoBits((ushort)(tagOperation.Bank));

            //Word pointer
            c1g2BlockPermaLock.BlockPointer = (uint)tagOperation.BlockPtr;

            //Block Mask
            if (null != tagOperation.Mask)
            {
                c1g2BlockPermaLock.BlockMask.data.AddRange(tagOperation.Mask);
            }
            else
            {
                c1g2BlockPermaLock.BlockMask.data.AddRange(new ushort[] { 0 });
            }

            //Read Lock
            c1g2BlockPermaLock.ReadLock = tagOperation.ReadLock;

            //Set the OpSpecID to a unique number.
            c1g2BlockPermaLock.OpSpecID = ++OpSpecID;

            return c1g2BlockPermaLock;
        }

        /// <summary>
        /// Create a opspec that erase a block of data
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_C1G2BlockErase</returns>
        private PARAM_C1G2BlockErase BuildBlockEraseOpSpec(Gen2.BlockErase tagOperation)
        {
            PARAM_C1G2BlockErase c1g2BlockErase = new PARAM_C1G2BlockErase();
            //AccessPassword
            c1g2BlockErase.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            //Memory Bank
            if (((ushort)(tagOperation.Bank)) > 3)
            {
                throw new Exception("Invalid argument");
            }
            c1g2BlockErase.MB = new LTKD.TwoBits((ushort)(tagOperation.Bank));

            c1g2BlockErase.WordCount = (ushort)tagOperation.WordCount;
            c1g2BlockErase.WordPointer = (ushort)tagOperation.WordPtr;

            // Set the OpSpecID to a unique number.
            c1g2BlockErase.OpSpecID = ++OpSpecID;
            return c1g2BlockErase;

        }

        /// <summary>
        /// Create a opspec for alien higgs2 partial load image 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_ThingMagicHiggs2PartialLoadImage</returns>
        private PARAM_ThingMagicHiggs2PartialLoadImage BuildHiggs2PartialLoadImage(Gen2.Alien.Higgs2.PartialLoadImage tagOperation)
        {
            PARAM_ThingMagicHiggs2PartialLoadImage higgs2PartialLoadImage = new PARAM_ThingMagicHiggs2PartialLoadImage();

            //access password to be written on tag
            higgs2PartialLoadImage.AccessPassword = tagOperation.AccessPassword;

            //access password to use to write on tag
            higgs2PartialLoadImage.CurrentAccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            //Kill Password to be written on tag
            higgs2PartialLoadImage.KillPassword = tagOperation.KillPassword;

            //EPC to be written
            string epc = ByteFormat.ToHex(tagOperation.Epc).Split('x')[1];
            higgs2PartialLoadImage.EPCData = LTKD.ByteArray.FromHexString(epc);

            //Set the OpSpecID to a unique number.
            higgs2PartialLoadImage.OpSpecID = ++OpSpecID;

            return higgs2PartialLoadImage;
        }

        /// <summary>
        /// Create a opspec for alien higgs2 full load image 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_ThingMagicHiggs2FullLoadImage</returns>
        private PARAM_ThingMagicHiggs2FullLoadImage BuildHiggs2FullLoadImage(Gen2.Alien.Higgs2.FullLoadImage tagOperation)
        {
            PARAM_ThingMagicHiggs2FullLoadImage higgs2FullLoadImage = new PARAM_ThingMagicHiggs2FullLoadImage();

            //access password to be written on tag
            higgs2FullLoadImage.AccessPassword = tagOperation.AccessPassword;

            //access password to use to write on tag
            higgs2FullLoadImage.CurrentAccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            //Kill Password to be written on tag
            higgs2FullLoadImage.KillPassword = tagOperation.KillPassword;

            //EPC to be written
            string epc = ByteFormat.ToHex(tagOperation.Epc).Split('x')[1];
            higgs2FullLoadImage.EPCData = LTKD.ByteArray.FromHexString(epc);

            //Set LockBits
            higgs2FullLoadImage.LockBits = tagOperation.LockBits;

            //Set PCWord 
            higgs2FullLoadImage.PCWord = tagOperation.PCWord;

            //Set the OpSpecID to a unique number.
            higgs2FullLoadImage.OpSpecID = ++OpSpecID;

            return higgs2FullLoadImage;
        }

        /// <summary>
        /// Create a opspec for alien higgs3 fast load image 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_ThingMagicHiggs3FastLoadImage</returns>
        private PARAM_ThingMagicHiggs3FastLoadImage BuildHiggs3FastLoadImage(Gen2.Alien.Higgs3.FastLoadImage tagOperation)
        {
            PARAM_ThingMagicHiggs3FastLoadImage higgs3FastLoadImage = new PARAM_ThingMagicHiggs3FastLoadImage();

            //access password to be written on tag
            higgs3FastLoadImage.AccessPassword = tagOperation.AccessPassword;

            //access password to use to write on tag
            higgs3FastLoadImage.CurrentAccessPassword = tagOperation.CurrentAccessPassword;

            //Kill Password to be written on tag
            higgs3FastLoadImage.KillPassword = tagOperation.KillPassword;

            //EPC to be written
            string epc = ByteFormat.ToHex(tagOperation.Epc).Split('x')[1];
            higgs3FastLoadImage.EPCData = LTKD.ByteArray.FromHexString(epc);

            //Set PCWord 
            higgs3FastLoadImage.PCWord = tagOperation.PCWord;

            //Set the OpSpecID to a unique number.
            higgs3FastLoadImage.OpSpecID = ++OpSpecID;

            return higgs3FastLoadImage;
        }

        /// <summary>
        /// Create a opspec for alien higgs3 load image 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_ThingMagicHiggs3LoadImage</returns>
        private PARAM_ThingMagicHiggs3LoadImage BuildHiggs3LoadImage(Gen2.Alien.Higgs3.LoadImage tagOperation)
        {
            PARAM_ThingMagicHiggs3LoadImage higgs3LoadImage = new PARAM_ThingMagicHiggs3LoadImage();

            //access password to be written on tag
            higgs3LoadImage.AccessPassword = tagOperation.AccessPassword;

            //access password to use to write on tag
            higgs3LoadImage.CurrentAccessPassword = tagOperation.CurrentAccessPassword;

            //Kill Password to be written on tag
            higgs3LoadImage.KillPassword = tagOperation.KillPassword;

            //EPC to be written
            string epc = ByteFormat.ToHex(tagOperation.EpcAndUserData).Split('x')[1];
            higgs3LoadImage.EPCAndUserData = LTKD.ByteArray.FromHexString(epc);

            //Set PCWord 
            higgs3LoadImage.PCWord = tagOperation.PCWord;

            //Set the OpSpecID to a unique number.
            higgs3LoadImage.OpSpecID = ++OpSpecID;

            return higgs3LoadImage;
        }

        /// <summary>
        /// Create a opspec for alien higgs3 block read lock 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_ThingMagicHiggs3BlockReadLock</returns>
        private PARAM_ThingMagicHiggs3BlockReadLock BuildHiggs3BlockReadLock(Gen2.Alien.Higgs3.BlockReadLock tagOperation)
        {
            PARAM_ThingMagicHiggs3BlockReadLock higgs3BlockReadLock = new PARAM_ThingMagicHiggs3BlockReadLock();

            //access password to use to write on tag
            higgs3BlockReadLock.AccessPassword = tagOperation.AccessPassword;

            //Set Lock Bits
            higgs3BlockReadLock.LockBits = tagOperation.LockBits;

            //Set the OpSpecID to a unique number.
            higgs3BlockReadLock.OpSpecID = ++OpSpecID;

            return higgs3BlockReadLock;
        }

        /// <summary>
        /// Create a opspec for nxp gen2 set read protect 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_Custom</returns>
        private PARAM_Custom BuildNxpGen2SetReadProtect(Gen2.NxpGen2TagOp.SetReadProtect tagOperation)
        {
            PARAM_ThingMagicNXPG2XSetReadProtect nxpG2XSetReadProtect = null;
            PARAM_ThingMagicNXPG2ISetReadProtect nxpG2ISetReadProtect = null;

            if (tagOperation is Gen2.NXP.G2X.SetReadProtect)
            {
                nxpG2XSetReadProtect = new PARAM_ThingMagicNXPG2XSetReadProtect();

                //Set access password
                nxpG2XSetReadProtect.AccessPassword = tagOperation.AccessPassword;

                //Set the OpSpecID to a unique number.
                nxpG2XSetReadProtect.OpSpecID = ++OpSpecID;

                return nxpG2XSetReadProtect;
            }
            else
            {
                nxpG2ISetReadProtect = new PARAM_ThingMagicNXPG2ISetReadProtect();

                //Set access password
                nxpG2ISetReadProtect.AccessPassword = tagOperation.AccessPassword;

                //Set the OpSpecID to a unique number.
                nxpG2ISetReadProtect.OpSpecID = ++OpSpecID;

                return nxpG2ISetReadProtect;
            }
        }

        /// <summary>
        /// Create a opspec for nxp gen2 reset read protect 
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_Custom</returns>
        private PARAM_Custom BuildNxpGen2ResetReadProtect(Gen2.NxpGen2TagOp.ResetReadProtect tagOperation)
        {
            PARAM_ThingMagicNXPG2XResetReadProtect nxpG2XResetReadProtect = null;
            PARAM_ThingMagicNXPG2IResetReadProtect nxpG2IResetReadProtect = null;

            if (tagOperation is Gen2.NXP.G2X.ResetReadProtect)
            {
                nxpG2XResetReadProtect = new PARAM_ThingMagicNXPG2XResetReadProtect();

                //Set access password
                nxpG2XResetReadProtect.AccessPassword = tagOperation.AccessPassword;

                //Set the OpSpecID to a unique number.
                nxpG2XResetReadProtect.OpSpecID = ++OpSpecID;

                return nxpG2XResetReadProtect;
            }
            else
            {
                nxpG2IResetReadProtect = new PARAM_ThingMagicNXPG2IResetReadProtect();

                //Set access password
                nxpG2IResetReadProtect.AccessPassword = tagOperation.AccessPassword;

                //Set the OpSpecID to a unique number.
                nxpG2IResetReadProtect.OpSpecID = ++OpSpecID;

                return nxpG2IResetReadProtect;
            }
        }

        /// <summary>
        /// Create a opspec for nxp gen2 change EAS
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_Custom</returns>
        private PARAM_Custom BuildNxpGen2ChangeEAS(Gen2.NxpGen2TagOp.ChangeEas tagOperation)
        {
            PARAM_ThingMagicNXPG2XChangeEAS nxpG2XChangeEAS = null;
            PARAM_ThingMagicNXPG2IChangeEAS nxpG2IChangeEAS = null;

            if (tagOperation is Gen2.NXP.G2X.ChangeEas)
            {
                nxpG2XChangeEAS = new PARAM_ThingMagicNXPG2XChangeEAS();

                //Set access password
                nxpG2XChangeEAS.AccessPassword = tagOperation.AccessPassword;

                //Set EASStatus
                nxpG2XChangeEAS.Reset = tagOperation.Reset;

                //Set the OpSpecID to a unique number.
                nxpG2XChangeEAS.OpSpecID = ++OpSpecID;

                return nxpG2XChangeEAS;
            }
            else
            {
                nxpG2IChangeEAS = new PARAM_ThingMagicNXPG2IChangeEAS();

                //Set access password
                nxpG2IChangeEAS.AccessPassword = tagOperation.AccessPassword;

                //Set EASStatus
                nxpG2IChangeEAS.Reset = tagOperation.Reset;

                //Set the OpSpecID to a unique number.
                nxpG2IChangeEAS.OpSpecID = ++OpSpecID;

                return nxpG2IChangeEAS;
            }
        }

        /// <summary>
        /// Create a opspec for nxp gen2 Calibrate
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_Custom</returns>
        private PARAM_Custom BuildNxpGen2Calibrate(Gen2.NxpGen2TagOp.Calibrate tagOperation)
        {
            PARAM_ThingMagicNXPG2XCalibrate nxpG2XCalibrate = null;
            PARAM_ThingMagicNXPG2ICalibrate nxpG2ICalibrate = null;

            if (tagOperation is Gen2.NXP.G2X.Calibrate)
            {
                nxpG2XCalibrate = new PARAM_ThingMagicNXPG2XCalibrate();

                //Set access password
                nxpG2XCalibrate.AccessPassword = tagOperation.AccessPassword;

                //Set the OpSpecID to a unique number.
                nxpG2XCalibrate.OpSpecID = ++OpSpecID;

                return nxpG2XCalibrate;
            }
            else
            {
                nxpG2ICalibrate = new PARAM_ThingMagicNXPG2ICalibrate();

                //Set access password
                nxpG2ICalibrate.AccessPassword = tagOperation.AccessPassword;

                //Set the OpSpecID to a unique number.
                nxpG2ICalibrate.OpSpecID = ++OpSpecID;

                return nxpG2ICalibrate;
            }
        }

        /// <summary>
        /// Create a opspec for nxp gen2 EAS alarm
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_Custom</returns>
        private PARAM_Custom BuildNxpGen2EASAlarm(Gen2.NxpGen2TagOp.EasAlarm tagOperation)
        {
            PARAM_ThingMagicNXPG2XEASAlarm nxpG2XEASAlarm = null;
            PARAM_ThingMagicNXPG2IEASAlarm nxpG2IEASAlarm = null;

            if (tagOperation is Gen2.NXP.G2X.EasAlarm)
            {
                nxpG2XEASAlarm = new PARAM_ThingMagicNXPG2XEASAlarm();

                //Set access password
                nxpG2XEASAlarm.AccessPassword = tagOperation.AccessPassword;

                //Set DivideRatio
                nxpG2XEASAlarm.DivideRatio = (ENUM_ThingMagicGen2DivideRatio)tagOperation.DivideRatio;

                //Set TrExt
                nxpG2XEASAlarm.PilotTone = Convert.ToBoolean(tagOperation.TrExt);
                //Gen2.TrExt.PILOTTONE = Gen2.TrExt

                // Set TagEncoding 
                nxpG2XEASAlarm.TagEncoding = (ENUM_ThingMagicGen2TagEncoding)tagOperation.TagEncoding;

                //Set the OpSpecID to a unique number.
                nxpG2XEASAlarm.OpSpecID = ++OpSpecID;

                return nxpG2XEASAlarm;
            }
            else
            {
                nxpG2IEASAlarm = new PARAM_ThingMagicNXPG2IEASAlarm();

                //Set access password
                nxpG2IEASAlarm.AccessPassword = tagOperation.AccessPassword;

                //Set DivideRatio
                nxpG2IEASAlarm.DivideRatio = (ENUM_ThingMagicGen2DivideRatio)tagOperation.DivideRatio;

                //Set TrExt
                nxpG2IEASAlarm.PilotTone = Convert.ToBoolean(tagOperation.TrExt);

                // Set TagEncoding 
                nxpG2IEASAlarm.TagEncoding = (ENUM_ThingMagicGen2TagEncoding)tagOperation.TagEncoding;

                //Set the OpSpecID to a unique number.
                nxpG2IEASAlarm.OpSpecID = ++OpSpecID;

                return nxpG2IEASAlarm;
            }
        }

        /// <summary>
        /// Create a opspec for nxp gen2i change config
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_Custom</returns>
        private PARAM_Custom BuildNxpG2IChangeConfig(Gen2.NXP.G2I.ChangeConfig tagOperation)
        {
            PARAM_ThingMagicNXPG2IChangeConfig nxpG2IChangeConfig = null;

            if (!(tagOperation is Gen2.NXP.G2I.ChangeConfig))
            {
                throw new FeatureNotSupportedException("ChangeConfig works only for G2I tags.");
            }
            else
            {
                nxpG2IChangeConfig = new PARAM_ThingMagicNXPG2IChangeConfig();

                //Set access password
                nxpG2IChangeConfig.AccessPassword = tagOperation.AccessPassword;

                //Build Thingmagic NXP config word
                PARAM_ThingMagicNXPConfigWord nxpConfigWord = new PARAM_ThingMagicNXPConfigWord();
                Gen2.NXP.G2I.ConfigWord config = new Gen2.NXP.G2I.ConfigWord();
                config = config.GetConfigWord(tagOperation.ConfigWord);

                nxpConfigWord.ConditionalReadRangeReduction_OnOff = config.ConditionalReadRangeReduction_onOff;
                nxpConfigWord.ConditionalReadRangeReduction_OpenShort = config.ConditionalReadRangeReduction_openShort;
                nxpConfigWord.DataMode = config.DataMode;
                nxpConfigWord.DigitalOutput = config.DigitalOutput;
                nxpConfigWord.ExternalSupply = config.ExternalSupply;
                nxpConfigWord.InvertDigitalOutput = config.InvertDigitalOutput;
                nxpConfigWord.MaxBackscatterStrength = config.MaxBackscatterStrength;
                nxpConfigWord.PrivacyMode = config.PrivacyMode;
                nxpConfigWord.PSFAlarm = config.PsfAlarm;
                nxpConfigWord.ReadProtectEPC = config.ReadProtectEPC;
                nxpConfigWord.ReadProtectTID = config.ReadProtectTID;
                nxpConfigWord.ReadProtectUser = config.ReadProtectUser;
                nxpConfigWord.TamperAlarm = config.TamperAlarm;
                nxpConfigWord.TransparentMode = config.TransparentMode;

                nxpG2IChangeConfig.ThingMagicNXPConfigWord = nxpConfigWord;

                //Set the OpSpecID to a unique number.
                nxpG2IChangeConfig.OpSpecID = ++OpSpecID;

                return nxpG2IChangeConfig;
            }
        }

        /// <summary>
        /// Create a opspec for Impinj monza4 qtreadwrite
        /// </summary>
        /// <param name="tagOperation"></param>
        /// <returns>PARAM_ThingMagicImpinjMonza4QTReadWrite</returns>
        private PARAM_ThingMagicImpinjMonza4QTReadWrite BuildMonza4QTReadWrite(Gen2.Impinj.Monza4.QTReadWrite tagOperation)
        {
            PARAM_ThingMagicImpinjMonza4QTReadWrite monza4QTReadWrite = new PARAM_ThingMagicImpinjMonza4QTReadWrite();

            //access password to be written on tag
            monza4QTReadWrite.AccessPassword = tagOperation.AccessPassword;

            // QT ControlByte
            PARAM_ThingMagicMonza4ControlByte monza4ControlByte = new PARAM_ThingMagicMonza4ControlByte();
            //build QTControlByte
            if ((tagOperation.ControlByte & 0x80) != 0)
            {
                monza4ControlByte.ReadWrite = true;
            }
            if ((tagOperation.ControlByte & 0x40) != 0)
            {
                monza4ControlByte.Persistance = true;
            }
            monza4QTReadWrite.ThingMagicMonza4ControlByte = monza4ControlByte;

            //QT PayLoad
            PARAM_ThingMagicMonza4Payload monza4Payload = new PARAM_ThingMagicMonza4Payload();
            //build QTPayload
            if ((tagOperation.PayloadWord & 0x8000) != 0)
            {
                monza4Payload.QT_SR = true;
            }
            if ((tagOperation.PayloadWord & 0x4000) != 0)
            {
                monza4Payload.QT_MEM = true;
            }
            monza4QTReadWrite.ThingMagicMonza4Payload = monza4Payload;

            //Set the OpSpecID to a unique number.
            monza4QTReadWrite.OpSpecID = ++OpSpecID;

            return monza4QTReadWrite;
        }

        /// <summary>
        /// Create a OpSpec that reads tag memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_ThingMagicISO180006BRead</returns>
        private PARAM_ThingMagicISO180006BRead BuildIso180006bReadDataOpSpec(Iso180006b.ReadData tagOperation)
        {
            PARAM_ThingMagicISO180006BRead iso18k6bRead = new PARAM_ThingMagicISO180006BRead();

            //Set the protocol to iso18k6b
            ParamSet("/reader/tagop/protocol", TagProtocol.ISO180006B);

            //Byte address
            //iso18k6bRead.ByteAddress = Convert.ToUInt16(tagOperation.byteAddress.ToString("X"));
            iso18k6bRead.ByteAddress = (ushort)tagOperation.byteAddress;

            //Byte length
            iso18k6bRead.ByteLen = (ushort)tagOperation.length;

            //Set the OpSpecID to a unique number.
            iso18k6bRead.OpSpecID = ++OpSpecID;

            return iso18k6bRead;
        }

        /// <summary>
        /// Create a OpSpec that writes on to the iso18k6b tag memory
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_ThingMagicISO180006BWrite</returns>
        private PARAM_ThingMagicISO180006BWrite BuildIso180006bWriteTagDataOpSpec(Iso180006b.WriteData tagOperation)
        {
            PARAM_ThingMagicISO180006BWrite iso18k6bWriteTagaData = new PARAM_ThingMagicISO180006BWrite();

            //Set the protocol to iso18k6b
            ParamSet("/reader/tagop/protocol", TagProtocol.ISO180006B);

            //Byte address
            //iso18k6bWriteTagaData.ByteAddress = Convert.ToUInt16(tagOperation.Address.ToString("X"));
            iso18k6bWriteTagaData.ByteAddress = (ushort)tagOperation.Address;

            //Data to be written
            iso18k6bWriteTagaData.WriteData = LTKD.ByteArray.FromHexString(ByteFormat.ToHex((byte[])tagOperation.Data).Split('x')[1].ToString());

            //Set the OpSpecID to a unique number.
            iso18k6bWriteTagaData.OpSpecID = ++OpSpecID;

            return iso18k6bWriteTagaData;
        }

        /// <summary>
        /// Create a OpSpec that Lock a byte of memory on an ISO180006B tag 
        /// </summary>
        /// <param name="tagOperation"> Tag operation</param>
        /// <returns>PARAM_ThingMagicISO180006BLock</returns>
        private PARAM_ThingMagicISO180006BLock BuildIso180006bLockTagOpSpec(Iso180006b.LockTag tagOperation)
        {
            PARAM_ThingMagicISO180006BLock iso18k6bLockTag = new PARAM_ThingMagicISO180006BLock();

            //Set the protocol to iso18k6b
            ParamSet("/reader/tagop/protocol", TagProtocol.ISO180006B);

            //Byte address
            //iso18k6bLockTag.Address = Convert.ToByte(tagOperation.Address.ToString("X") );
            iso18k6bLockTag.Address = (byte)tagOperation.Address;

            //Set the OpSpecID to a unique number.
            iso18k6bLockTag.OpSpecID = ++OpSpecID;

            return iso18k6bLockTag;
        }

        /// <summary>
        /// Build IDsSl900A common header
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ACommandRequest</returns>
        private PARAM_ThingMagicIDSSL900ACommandRequest BuildIDsSL900aCommandRequest(Gen2.IDS.SL900A tagOperation)
        {
            PARAM_ThingMagicIDSSL900ACommandRequest cmdRequest = new PARAM_ThingMagicIDSSL900ACommandRequest();

            //Set AccessPassword
            cmdRequest.AccessPassword = tagOperation.AccessPassword;

            //Set CommandCode
            cmdRequest.CommandCode = tagOperation.CommandCode;

            //Set IDSPassword
            cmdRequest.IDSPassword = tagOperation.Password;

            //Set PasswordLevel
            cmdRequest.PasswordLevel = (ENUM_ThingMagicCustomIDSPasswordLevel)tagOperation.PasswordLevel;

            //Set OpSpecId
            cmdRequest.OpSpecID = ++OpSpecID;

            return cmdRequest;
        }

        private PARAM_ThingMagicNXPCommandRequest BuildNXPCommandRequest(Gen2.NXP.AES.Authenticate authenticate)
        {
            //Construct and initialize ThingMagicNXPCommandRequest            
            PARAM_ThingMagicNXPCommandRequest cmdRequest = new PARAM_ThingMagicNXPCommandRequest();

            //Set OpSpecId
            cmdRequest.OpSpecID = ++OpSpecID;

            cmdRequest.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            return cmdRequest;
        }

        private PARAM_ThingMagicNXPCommandRequest BuildNXPReadBufferCommandRequest(Gen2.NXP.AES.ReadBuffer readBuffer)
        {
            //Construct and initialize ThingMagicNXPCommandRequest            
            PARAM_ThingMagicNXPCommandRequest cmdRequest = new PARAM_ThingMagicNXPCommandRequest();

            //Set OpSpecId
            cmdRequest.OpSpecID = ++OpSpecID;

            cmdRequest.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            return cmdRequest;
        }

        private PARAM_ThingMagicNXPCommandRequest BuildNXPUntraceableCommandRequest(Gen2.NXP.AES.Untraceable untraceable)
        {
            //Construct and initialize ThingMagicNXPCommandRequest            
            PARAM_ThingMagicNXPCommandRequest cmdRequest = new PARAM_ThingMagicNXPCommandRequest();

            //Set OpSpecId
            cmdRequest.OpSpecID = ++OpSpecID;

            cmdRequest.AccessPassword = ((Gen2.Password)(ParamGet("/reader/gen2/accessPassword"))).Value;

            return cmdRequest;
        }

        /// <summary>
        ///  Create a OpSpec writes the calibration block
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ASetCalibrationData</returns>
        private PARAM_ThingMagicIDSSL900ASetCalibrationData BuildIDsSL900aSetCalibrationDataTagOpSpec(Gen2.IDS.SL900A.SetCalibrationData tagOperation)
        {
            PARAM_ThingMagicIDSSL900ASetCalibrationData setCalibrationData = new PARAM_ThingMagicIDSSL900ASetCalibrationData();

            //Set Command Request
            setCalibrationData.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

            //Set Calibration Data
            PARAM_ThingMagicIDSCalibrationData calibrationData = new PARAM_ThingMagicIDSCalibrationData();
            calibrationData.raw = tagOperation.Cal.Raw;
            calibrationData.coars1 = tagOperation.Cal.Coarse1;
            calibrationData.coars2 = tagOperation.Cal.Coarse2;
            calibrationData.df = tagOperation.Cal.Df;
            calibrationData.excRes = tagOperation.Cal.ExcRes;
            calibrationData.gndSwitch = tagOperation.Cal.GndSwitch;
            calibrationData.irlev = tagOperation.Cal.Irlev;
            calibrationData.selp12 = tagOperation.Cal.Selp12;
            calibrationData.selp22 = tagOperation.Cal.Selp22;
            calibrationData.swExtEn = tagOperation.Cal.SwExtEn;
            setCalibrationData.ThingMagicIDSCalibrationData = calibrationData;

            return setCalibrationData;
        }

        /// <summary>
        /// Create a OpSpec that stops the logging procedure
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AEndLog</returns>
        private PARAM_ThingMagicIDSSL900AEndLog BuildIDsSL900aEndLogTagOpSpec(Gen2.IDS.SL900A.EndLog tagOperation)
        {
            PARAM_ThingMagicIDSSL900AEndLog endLog = new PARAM_ThingMagicIDSSL900AEndLog();
            //Set Command Request
            endLog.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return endLog;
        }

        /// <summary>
        /// Create a OpSpec that sets the delay time and application Data fields
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AInitialize</returns>
        private PARAM_ThingMagicIDSSL900AInitialize BuildIDsSL900aInitializeTagOpSpec(Gen2.IDS.SL900A.Initialize tagOperation)
        {
            PARAM_ThingMagicIDSSL900AInitialize initialize = new PARAM_ThingMagicIDSSL900AInitialize();

            //Set Command Request
            initialize.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

            //Set Application Data
            PARAM_ThingMagicIDSApplicationData appData = new PARAM_ThingMagicIDSApplicationData();
            appData.numberOfWords = tagOperation.AppData.NumberOfWords;
            appData.brokenWordPointer = tagOperation.AppData.BrokenWordPointer;
            initialize.ThingMagicIDSApplicationData = appData;

            //Set Delay Time
            PARAM_ThingMagicIDSDelayTime delayTime = new PARAM_ThingMagicIDSDelayTime();
            delayTime.delayMode = Convert.ToByte(tagOperation.DelayTime.Mode);
            delayTime.delayTime = tagOperation.DelayTime.Time;
            delayTime.timerEnable = tagOperation.DelayTime.IrqTimerEnable;
            initialize.ThingMagicIDSDelayTime = delayTime;

            return initialize;
        }

        /// <summary>
        /// Create a OpSpec that sets the logging form
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ASetLogMode</returns>
        private PARAM_ThingMagicIDSSL900ASetLogMode BuildIDsSL900aSetLogModeTagOpSpec(Gen2.IDS.SL900A.SetLogMode tagOperation)
        {
            PARAM_ThingMagicIDSSL900ASetLogMode setLogMode = new PARAM_ThingMagicIDSSL900ASetLogMode();

            //Set Command Request
            setLogMode.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

            //set Battery Sensor
            setLogMode.BattEnable = tagOperation.BattEnable;
            //Set EXT1 external sensor
            setLogMode.Ext1Enable = tagOperation.Ext1Enable;
            //Set EXT2 external sensor
            setLogMode.Ext2Enable = tagOperation.Ext2Enable;
            //Set Logging interval
            setLogMode.LogInterval = tagOperation.LogInterval;
            //Set Logging format
            setLogMode.LoggingForm = (ENUM_ThingMagicCustomIDSLoggingForm)tagOperation.Form;
            //Set StorageRule
            setLogMode.StorageRule = (ENUM_ThingMagicCustomIDSStorageRule)tagOperation.Storage;
            //Set temperature sensor
            setLogMode.TempEnable = tagOperation.TempEnable;

            return setLogMode;
        }

        /// <summary>
        /// Create a OpSpec that starts the logging process
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AStartLog</returns>
        private PARAM_ThingMagicIDSSL900AStartLog BuildIDsSL900aStartLogTagOpSpec(Gen2.IDS.SL900A.StartLog tagOperation)
        {
            PARAM_ThingMagicIDSSL900AStartLog startLog = new PARAM_ThingMagicIDSSL900AStartLog();
            //Set Command Request
            startLog.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            //Set Start time
            startLog.StartTime = Convert.ToUInt32(ToSL900aTime(tagOperation.StartTime));

            return startLog;
        }

        /// <summary>
        /// Create a OpSpec that writes the sensor front end parameters to the memory
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ASetSFEParams</returns>
        private PARAM_ThingMagicIDSSL900ASetSFEParams BuildIDsSL900aSetSFEParamsTagOpSpec(Gen2.IDS.SL900A.SetSfeParameters tagOperation)
        {
            PARAM_ThingMagicIDSSL900ASetSFEParams sfeParameter = new PARAM_ThingMagicIDSSL900ASetSFEParams();

            //Set Command Request
            sfeParameter.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

            PARAM_ThingMagicIDSSFEParam sfeParam = new PARAM_ThingMagicIDSSFEParam();
            sfeParam.raw = tagOperation.Sfe.Raw;
            sfeParam.AutoRangeDisable = tagOperation.Sfe.AutorangeDisable;
            sfeParam.Ext1 = tagOperation.Sfe.Ext1;
            sfeParam.Ext2 = tagOperation.Sfe.Ext2;
            sfeParam.SFEType = ENUM_ThingMagicCustomIDSSFEType.IDSSL900A_SFE_RANG;
            sfeParam.VerifySensorID = tagOperation.Sfe.VerifySensorID;
            sfeParam.range = tagOperation.Sfe.Rang;
            sfeParam.seti = tagOperation.Sfe.Seti;
            sfeParameter.ThingMagicIDSSFEParam = sfeParam;

            return sfeParameter;
        }

        /// <summary>
        /// Create a OpSpec that writes 4 log limits
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ASetLogLimits</returns>
        private PARAM_ThingMagicIDSSL900ASetLogLimits BuildIDsSL900aSetLogLimitsTagOpSpec(Gen2.IDS.SL900A.SetLogLimit tagOperation)
        {
            PARAM_ThingMagicIDSSL900ASetLogLimits setLogLimit = new PARAM_ThingMagicIDSSL900ASetLogLimits();

            //Set Log Limit Data
            PARAM_ThingMagicIDSLogLimits logLimits = new PARAM_ThingMagicIDSLogLimits();
            logLimits.extremeLower = tagOperation.LogLimits.EXTREMELOWERLIMIT;
            logLimits.upper = tagOperation.LogLimits.UPPERLIMIT;
            logLimits.extremeUpper = tagOperation.LogLimits.EXTREMEUPPERLIMIT;
            logLimits.lower = tagOperation.LogLimits.LOWERLIMIT;
            setLogLimit.ThingMagicIDSLogLimits = logLimits;
            //Set Command Request
            setLogLimit.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return setLogLimit;
        }

        /// <summary>
        /// Create a OpSpec that writes 32bit password
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ASetIDSPassword</returns>
        private PARAM_ThingMagicIDSSL900ASetIDSPassword BuildIDsSL900aSetIDSPasswordTagOpSpec(Gen2.IDS.SL900A.SetPassword tagOperation)
        {
            PARAM_ThingMagicIDSSL900ASetIDSPassword password = new PARAM_ThingMagicIDSSL900ASetIDSPassword();

            //Set Command Request
            password.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            //Set IDS new passwordLevel
            password.NewPasswordLevel = (ENUM_ThingMagicCustomIDSPasswordLevel)tagOperation.newPasswordLevel;
            //Set IDS new password
            password.NewIDSPassword = tagOperation.newPassword;
            return password;
        }

        /// <summary>
        /// Create a OpSpec that gets the sensor value - internal, external1 or external2
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900ASensorValue</returns>
        private PARAM_ThingMagicIDSSL900ASensorValue BuildIDsSL900aSensorValueTagOpSpec(Gen2.IDS.SL900A.GetSensorValue tagOperation)
        {
            PARAM_ThingMagicIDSSL900ASensorValue sensorValue = new PARAM_ThingMagicIDSSL900ASensorValue();
            //set Sensortype
            sensorValue.SensorType = (ENUM_ThingMagicCustomIDSSensorType)tagOperation.SensorType;
            //Set Command Request
            sensorValue.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return sensorValue;
        }

        /// <summary>
        /// Create a OpSpec that gets the voltage level of the battery
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AGetBatteryLevel</returns>
        private PARAM_ThingMagicIDSSL900AGetBatteryLevel BuildIDsSL900aBatteryLevelTagOpSpec(Gen2.IDS.SL900A.GetBatteryLevel tagOperation)
        {
            PARAM_ThingMagicIDSSL900AGetBatteryLevel batteryLevel = new PARAM_ThingMagicIDSSL900AGetBatteryLevel();
            //Set battery retrigger
            batteryLevel.BatteryTrigger = (byte)tagOperation.batteryType;
            //Set command request
            batteryLevel.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return batteryLevel;
        }

        /// <summary>
        /// Create a OpSpec that gets the authentication
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AGetBatteryLevel</returns>
        private PARAM_ThingMagicNXPAuthentication BuildNXPAuthenticationTagOpSpec(Gen2.NXP.AES.Authenticate tagOperation)
        {
            //Construct and initialize ThingMagicNXPAuthentication
            PARAM_ThingMagicNXPAuthentication authenticate = new PARAM_ThingMagicNXPAuthentication();

            //Set command request
            authenticate.ThingMagicNXPCommandRequest = BuildNXPCommandRequest((tagOperation));
            authenticate.authType = (ENUM_ThingMagicCustomNXPAuthenticationType)tagOperation.authenticationType;
            authenticate.subCommand = tagOperation.SubCommand;

            if (tagOperation.authenticationType == Gen2.NXP.AES.authType.TAM1)
            {
                PARAM_ThingMagicNXPTAM1AuthenticationData tam1AuthData = new PARAM_ThingMagicNXPTAM1AuthenticationData();
                Gen2.NXP.AES.Tam1Authentication tam1Auth;
                tam1Auth = tagOperation.tam1;
                tam1AuthData.Authentication = tam1Auth.Authentication;
                tam1AuthData.CSI = tam1Auth.CSI;
                tam1AuthData.keyID = tam1Auth.keyID;
                tam1AuthData.KeyLength = tam1Auth.KeyLength;
                tam1AuthData.Key = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(tam1Auth.Key));
                authenticate.ThingMagicNXPTAM1AuthenticationData = tam1AuthData;
            }
            else if (tagOperation.authenticationType == Gen2.NXP.AES.authType.TAM2)
            {
                PARAM_ThingMagicNXPTAM2AuthenticationData tam2AuthData = new PARAM_ThingMagicNXPTAM2AuthenticationData();
                Gen2.NXP.AES.Tam2Authentication tam2Auth;
                tam2Auth = tagOperation.tam2;
                PARAM_ThingMagicNXPTAM1AuthenticationData tam1AuthData = new PARAM_ThingMagicNXPTAM1AuthenticationData();
                tam1AuthData.Authentication = tam2Auth.Authentication;
                tam1AuthData.CSI = tam2Auth.CSI;
                tam1AuthData.keyID = tam2Auth.keyID;
                tam1AuthData.KeyLength = tam2Auth.KeyLength;
                tam1AuthData.Key = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(tam2Auth.Key));
                tam2AuthData.offset = tam2Auth.Offset;
                tam2AuthData.ProtMode = (byte)tam2Auth.ProtMode;
                tam2AuthData.BlockCount = (byte)tam2Auth.BlockCount;
                tam2AuthData.profile = (ENUM_ThingMagicNXPProfileType)tam2Auth.Profile;
                tam2AuthData.ThingMagicNXPTAM1AuthenticationData = tam1AuthData;
                authenticate.ThingMagicNXPTAM2AuthenticationData = tam2AuthData;
            }
            return authenticate;
        }

        private PARAM_ThingMagicNXPReadbuffer BuildNXPAESReadBuffer(Gen2.NXP.AES.ReadBuffer tagOperation)
        {
            //Construct and initialize ThingMagicNXPReadBuffer
            PARAM_ThingMagicNXPReadbuffer nxpReadBuffer = new PARAM_ThingMagicNXPReadbuffer();
            nxpReadBuffer.ThingMagicNXPCommandRequest = BuildNXPReadBufferCommandRequest((tagOperation));
            nxpReadBuffer.subCommand = tagOperation.SubCommand;
            nxpReadBuffer.wordPointer = tagOperation.WordPointer;
            nxpReadBuffer.bitCount = tagOperation.BitCount;
            nxpReadBuffer.Authtype = (ENUM_ThingMagicCustomNXPAuthenticationType)tagOperation.authenticationType;
            if (tagOperation.authenticationType == Gen2.NXP.AES.authType.TAM1)
            {
                PARAM_ThingMagicNXPTAM1AuthenticationData tam1AuthData = new PARAM_ThingMagicNXPTAM1AuthenticationData();
                Gen2.NXP.AES.Tam1Authentication tam1Auth;
                tam1Auth = tagOperation.tam1;
                tam1AuthData.Authentication = tam1Auth.Authentication;
                tam1AuthData.CSI = tam1Auth.CSI;
                tam1AuthData.keyID = tam1Auth.keyID;
                tam1AuthData.KeyLength = tam1Auth.KeyLength;
                tam1AuthData.Key = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(tam1Auth.Key));
                nxpReadBuffer.ThingMagicNXPTAM1AuthenticationData = tam1AuthData;
            }
            else if (tagOperation.authenticationType == Gen2.NXP.AES.authType.TAM2)
            {
                PARAM_ThingMagicNXPTAM2AuthenticationData tam2AuthData = new PARAM_ThingMagicNXPTAM2AuthenticationData();
                Gen2.NXP.AES.Tam2Authentication tam2Auth;
                tam2Auth = tagOperation.tam2;
                PARAM_ThingMagicNXPTAM1AuthenticationData tam1AuthData = new PARAM_ThingMagicNXPTAM1AuthenticationData();
                tam1AuthData.Authentication = tam2Auth.Authentication;
                tam1AuthData.CSI = tam2Auth.CSI;
                tam1AuthData.keyID = tam2Auth.keyID;
                tam1AuthData.KeyLength = tam2Auth.KeyLength;
                tam1AuthData.Key = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(tam2Auth.Key));
                tam2AuthData.ThingMagicNXPTAM1AuthenticationData = tam1AuthData;
                tam2AuthData.offset = tam2Auth.Offset;
                tam2AuthData.ProtMode = (byte)tam2Auth.ProtMode;
                tam2AuthData.BlockCount = (byte)tam2Auth.BlockCount;
                tam2AuthData.profile = (ENUM_ThingMagicNXPProfileType)tam2Auth.Profile;
                nxpReadBuffer.ThingMagicNXPTAM2AuthenticationData = tam2AuthData;
            }
            return nxpReadBuffer;
        }
        private PARAM_ThingMagicNXPUntraceable BuildNXPAESUntraceable(Gen2.NXP.AES.Untraceable tagOperation)
        {
            PARAM_ThingMagicNXPUntraceable nxpUntraceable = new PARAM_ThingMagicNXPUntraceable();
            nxpUntraceable.ThingMagicNXPCommandRequest = BuildNXPUntraceableCommandRequest((tagOperation));
            Gen2.NXP.AES.Untraceable untraceable;
            untraceable = tagOperation;
            PARAM_ThingMagicNXPUntraceableAuthentication nxp = new PARAM_ThingMagicNXPUntraceableAuthentication();
            nxp.authType = (ENUM_ThingMagicCustomNXPUntraceableAuthType)tagOperation.untraceType;
            if (tagOperation.untraceType == Gen2.NXP.AES.Untraceable.UntraceType.UNTRACEABLE_WITH_AUTHENTICATION)
            {
                PARAM_ThingMagicNXPTAM1AuthenticationData tam1AuthData = new PARAM_ThingMagicNXPTAM1AuthenticationData();
                Gen2.NXP.AES.Tam1Authentication tam1Auth;
                tam1Auth = tagOperation.auth;
                tam1AuthData.Authentication = tam1Auth.Authentication;
                tam1AuthData.CSI = tam1Auth.CSI;
                tam1AuthData.keyID = tam1Auth.keyID;
                tam1AuthData.KeyLength = tam1Auth.KeyLength;
                tam1AuthData.Key = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(tam1Auth.Key));
                nxp.ThingMagicNXPTAM1AuthenticationData = tam1AuthData;
            }
            nxp.accessPassword = untraceable.aes.AccessPassword;
            nxpUntraceable.ThingMagicNXPUntraceableAuthentication = nxp;
            nxpUntraceable.epc = (ENUM_ThingMagicCustomNXPUntraceableEPC)untraceable.epc;
            nxpUntraceable.epcLength = (uint)untraceable.epcLen;
            nxpUntraceable.tid = (ENUM_ThingMagicCustomNXPUntraceableTID)untraceable.tid;
            nxpUntraceable.userMemory = (ENUM_ThingMagicCustomNXPUntraceableUserMemory)untraceable.user;
            nxpUntraceable.range = (ENUM_ThingMagicCustomNXPUntraceableRange)untraceable.range;
            nxpUntraceable.subCommand = tagOperation.SubCommand;
            return nxpUntraceable;
        }
        /// <summary>
        /// Create a OpSpec reads the calibration field and the SFE parameter field
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AGetCalibrationData</returns>
        private PARAM_ThingMagicIDSSL900AGetCalibrationData BuildIDsSL900aGetCalibrationDataTagOpSpec(Gen2.IDS.SL900A.GetCalibrationData tagOperation)
        {
            PARAM_ThingMagicIDSSL900AGetCalibrationData getCalibrationData = new PARAM_ThingMagicIDSSL900AGetCalibrationData();
            //Set Command Request
            getCalibrationData.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return getCalibrationData;
        }

        /// <summary>
        /// Create a OpSpec that reads the status of the logging process
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AGetLogState</returns>
        private PARAM_ThingMagicIDSSL900AGetLogState BuildIDsSL900aLoggingFormTagOpSpec(Gen2.IDS.SL900A.GetLogState tagOperation)
        {
            PARAM_ThingMagicIDSSL900AGetLogState logState = new PARAM_ThingMagicIDSSL900AGetLogState();
            //Set Command Request
            logState.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return logState;
        }

        /// <summary>
        /// Create a OpSpec that reads the current system setup of the chip
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSL900AGetMeasurementSetup</returns>
        private PARAM_ThingMagicIDSSL900AGetMeasurementSetup BuildIDSsL900aGetMeasurementSetupTagOpSpec(Gen2.IDS.SL900A.GetMeasurementSetup tagOperation)
        {
            PARAM_ThingMagicIDSSL900AGetMeasurementSetup measurementSetup = new PARAM_ThingMagicIDSSL900AGetMeasurementSetup();
            //Set Command Request
            measurementSetup.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return measurementSetup;
        }

        /// <summary>
        /// Create a OpSpec that reads and write data from FIFO
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_Custom Object</returns>
        private PARAM_Custom BuildIDsSL900aAccessFifoTagOpSpec(Gen2.IDS.SL900A.AccessFifo tagOperation)
        {
            if (tagOperation is Gen2.IDS.SL900A.AccessFifoRead)
            {
                Gen2.IDS.SL900A.AccessFifoRead op = (Gen2.IDS.SL900A.AccessFifoRead)tagOperation;

                PARAM_ThingMagicIDSSL900AAccessFIFORead fifoRead = new PARAM_ThingMagicIDSSL900AAccessFIFORead();

                //Set Command Request
                fifoRead.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

                //Set FIFO Read Length
                fifoRead.FIFOReadLength = op.Length;

                return fifoRead;
            }
            else if (tagOperation is Gen2.IDS.SL900A.AccessFifoStatus)
            {
                PARAM_ThingMagicIDSSL900AAccessFIFOStatus fifoStatus = new PARAM_ThingMagicIDSSL900AAccessFIFOStatus();

                //Set Command Request
                fifoStatus.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

                return fifoStatus;
            }
            else if (tagOperation is Gen2.IDS.SL900A.AccessFifoWrite)
            {
                Gen2.IDS.SL900A.AccessFifoWrite op = (Gen2.IDS.SL900A.AccessFifoWrite)tagOperation;

                PARAM_ThingMagicIDSSL900AAccessFIFOWrite fifoWrite = new PARAM_ThingMagicIDSSL900AAccessFIFOWrite();

                //Set Command Request
                fifoWrite.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));

                //Set write payload
                fifoWrite.writePayLoad = LTKD.ByteArray.FromHexString(ByteFormat.ToHex(op.Payload).Split('x')[1]);

                return fifoWrite;
            }
            else
            {
                throw new Exception("Unsupported AccessFifo tagop: " + tagOperation);
            }
        }

        /// <summary>
        /// buildIDSSL900ASetShelfLife
        /// </summary>
        /// <param name="tagOperation">Tag Operation</param>
        /// <returns>PARAM_ThingMagicIDSSetShelfLife</returns>
        private PARAM_ThingMagicIDSSetShelfLife BuildIDsSL900aSetShelfLifeTagOpSpec(Gen2.IDS.SL900A.SetShelfLife tagOperation)
        {
            PARAM_ThingMagicIDSSetShelfLife setShelfLife = new PARAM_ThingMagicIDSSetShelfLife();

            //Set SHelfLifeBlock0
            PARAM_ThingMagicIDSSLBlock0 slBlock0 = new PARAM_ThingMagicIDSSLBlock0();
            slBlock0.Ea = tagOperation.shelfLifeBlock0.EA;
            slBlock0.TimeMax = tagOperation.shelfLifeBlock0.TMAX;
            slBlock0.TimeMin = tagOperation.shelfLifeBlock0.TMIN;
            slBlock0.TimeStd = tagOperation.shelfLifeBlock0.TSTD;
            slBlock0.raw = tagOperation.shelfLifeBlock0.Raw;
            setShelfLife.ThingMagicIDSSLBlock0 = slBlock0;

            //Set ShelfLifeBlock1
            PARAM_ThingMagicIDSSLBlock1 slBlock1 = new PARAM_ThingMagicIDSSLBlock1();
            slBlock1.RFU = (byte)0;
            slBlock1.SLInit = tagOperation.shelfLifeBlock1.SLINIT;
            slBlock1.TInit = tagOperation.shelfLifeBlock1.TINIT;
            slBlock1.algorithmEnable = tagOperation.shelfLifeBlock1.ENABLEALGORITHM;
            slBlock1.SensorID = tagOperation.shelfLifeBlock1.SENSORID;
            slBlock1.enableNegative = tagOperation.shelfLifeBlock1.ENABLENEGATIVE;
            slBlock1.raw = tagOperation.shelfLifeBlock1.Raw;
            setShelfLife.ThingMagicIDSSLBlock1 = slBlock1;

            //Set Command Request
            setShelfLife.ThingMagicIDSSL900ACommandRequest = BuildIDsSL900aCommandRequest((tagOperation));
            return setShelfLife;
        }

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

        private PARAM_ThingMagicDenatranIAVActivateSecureMode BuildIAVActivateSecureMode(Gen2.Denatran.IAV.ActivateSecureMode tagOp)
        {
            PARAM_ThingMagicDenatranIAVActivateSecureMode activateSecureMode = new PARAM_ThingMagicDenatranIAVActivateSecureMode();
            //Set IAVCommandRequest
            activateSecureMode.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return activateSecureMode;
        }

        private PARAM_ThingMagicDenatranIAVActivateSiniavMode BuildIAVActivateSiniavMode(Gen2.Denatran.IAV.ActivateSiniavMode tagOp)
        {
            PARAM_ThingMagicDenatranIAVActivateSiniavMode activateSiniavMode = new PARAM_ThingMagicDenatranIAVActivateSiniavMode();
            //Set IAVCommandRequest
            activateSiniavMode.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return activateSiniavMode;
        }

        private PARAM_ThingMagicDenatranIAVAuthenticateOBU BuildIAVAuthenticateOBU(Gen2.Denatran.IAV.AuthenticateOBU tagOp)
        {
            PARAM_ThingMagicDenatranIAVAuthenticateOBU authenticateOBU = new PARAM_ThingMagicDenatranIAVAuthenticateOBU();
            //Set IAVCommandRequest
            authenticateOBU.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return authenticateOBU;
        }

        private PARAM_ThingMagicDenatranIAVOBUAuthenticateID BuildIAVOBUAuthenticateID(Gen2.Denatran.IAV.OBUAuthID tagOp)
        {
            PARAM_ThingMagicDenatranIAVOBUAuthenticateID authenticateID = new PARAM_ThingMagicDenatranIAVOBUAuthenticateID();
            //Set IAVCommandRequest
            authenticateID.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return authenticateID;
        }

        private PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1 BuildIAVOBUAuthenticateFullPass1(Gen2.Denatran.IAV.OBUAuthFullPass1 tagOp)
        {
            PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1 authenticateFullPass1 = new PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass1();
            //Set IAVCommandRequest
            authenticateFullPass1.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return authenticateFullPass1;
        }

        private PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2 BuildIAVOBUAuthenticateFullPass2(Gen2.Denatran.IAV.OBUAuthFullPass2 tagOp)
        {
            PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2 authenticateFullPass2 = new PARAM_ThingMagicDenatranIAVOBUAuthenticateFullPass2();
            //Set IAVCommandRequest
            authenticateFullPass2.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return authenticateFullPass2;
        }

        private PARAM_ThingMagicDenatranIAVOBUReadFromMemMap BuildIAVOBUReadFromMemMap(Gen2.Denatran.IAV.OBUReadFromMemMap tagOp)
        {
            PARAM_ThingMagicDenatranIAVOBUReadFromMemMap readFromMemMap = new PARAM_ThingMagicDenatranIAVOBUReadFromMemMap();
            //Set IAVCommandRequest
            readFromMemMap.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return readFromMemMap;
        }

        private PARAM_ThingMagicDenatranIAVOBUWriteToMemMap BuildIAVOBUWriteToMemMap(Gen2.Denatran.IAV.OBUWriteToMemMap tagOp)
        {
            PARAM_ThingMagicDenatranIAVOBUWriteToMemMap writeToMemMap = new PARAM_ThingMagicDenatranIAVOBUWriteToMemMap();
            //Set IAVCommandRequest
            writeToMemMap.ThingMagicDenatranIAVCommandRequest = BuildInitIAVCommandRequest(tagOp);
            return writeToMemMap;
        }

        private PARAM_ThingMagicDenatranIAVCommandRequest BuildInitIAVCommandRequest(Gen2.Denatran.IAV tagOp)
        {
            PARAM_ThingMagicDenatranIAVCommandRequest commandRequest = new PARAM_ThingMagicDenatranIAVCommandRequest();
            commandRequest.PayLoad = tagOp.Payload;
            commandRequest.OpSpecID = ++OpSpecID;
            return commandRequest;
        }

        /// <summary>
        /// Add the AccessSpec to the reader.
        /// </summary>
        /// <param name="accessSpec">AccessS pec</param>
        /// <returns>true/false</returns>
        public bool AddAccessSpec(PARAM_AccessSpec accessSpec)
        {
            MSG_ADD_ACCESSSPEC_RESPONSE response;
            MSG_ADD_ACCESSSPEC accessSpecMsg = new MSG_ADD_ACCESSSPEC();
            try
            {
                accessSpecMsg.AccessSpec = accessSpec;
                response = (MSG_ADD_ACCESSSPEC_RESPONSE)SendLlrpMessage(accessSpecMsg);
                if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                {
                    return true;
                }
                else
                {
                    if (continuousReading)
                    {
                        //For Embedded Operation
                        notifyExceptionListeners(new ReaderException(response.LLRPStatus.ErrorDescription));
                        return false;
                    }
                    //For Standalone Operation
                    throw new ReaderException(response.LLRPStatus.ErrorDescription);
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }

        /// <summary>
        /// Enable the AccessSpec
        /// </summary>
        /// <param name="accessSpecID">accessSpecID</param>
        /// <returns>true/false</returns>
        private bool EnableAccessSpec(uint accessSpecID)
        {
            MSG_ENABLE_ACCESSSPEC_RESPONSE response;
            MSG_ENABLE_ACCESSSPEC enableAccessSpec = new MSG_ENABLE_ACCESSSPEC();
            try
            {
                enableAccessSpec.AccessSpecID = accessSpecID;
                response = (MSG_ENABLE_ACCESSSPEC_RESPONSE)SendLlrpMessage(enableAccessSpec);
                if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }

        /// <summary>
        /// Disable the ROSpec.
        /// </summary>
        /// <param name="roSpecId">Ro spec id to disable</param>
        /// <returns>true/false</returns>
        private bool DisableROSpec(uint roSpecId)
        {
            MSG_DISABLE_ROSPEC_RESPONSE response;
            MSG_DISABLE_ROSPEC enable = new MSG_DISABLE_ROSPEC();
            enable.ROSpecID = roSpecId;
            try
            {
                response = (MSG_DISABLE_ROSPEC_RESPONSE)SendLlrpMessage(enable);
                if (response.LLRPStatus.StatusCode == ENUM_StatusCode.M_Success)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (ReaderCommException rce)
            {
                throw new ReaderException(rce.Message);
            }
        }

        #region Misc Utility Methods

        #region ValidateAntenna
        /// <summary>
        /// Is requested antenna a valid antenna?
        /// </summary>
        /// <param name="reqAnt">Requested antenna</param>
        /// <returns>reqAnt if it is in the set of valid antennas, else throws ArgumentException</returns>
        private int ValidateAntenna(int reqAnt)
        {
            return ValidateParameter<int>(reqAnt, (int[])ParamGet("/reader/antenna/portList"), "Invalid antenna");
        }

        #endregion
        #endregion Misc Utility Methods
        ///// <summary>
        ///// Custom region Configuration
        ///// </summary>
        ///// <param name="LBTEnable"></param>
        ///// <param name="LBTThreshold"></param>
        ///// <param name="dwellTimeEnable"></param>
        ///// <param name="dwellTime"></param>
        //public override void regionConfiguration(bool LBTEnable, Int16 LBTThreshold, bool dwellTimeEnable, UInt16 dwellTime)
        //{
           
        //}
    }
}
