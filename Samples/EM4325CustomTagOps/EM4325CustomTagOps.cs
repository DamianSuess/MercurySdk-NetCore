using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace EM4325CustomTagOps
{
    /// <summary>
    /// Sample program thatthe functionality of EM4325 custom tag operations
    /// </summary>
    class EM4325CustomTagOps
    {
        static int[] antennaList = null;
        static Reader r = null;
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example for UHF: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'"
            }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            bool enableFilter = false;
            TagFilter filter = null;
            // Program setup
            if (1 > args.Length)
            {
                Usage();
            }
            for (int nextarg = 1; nextarg < args.Length; nextarg++)
            {
                string arg = args[nextarg];
                if (arg.Equals("--ant"))
                {
                    if (null != antennaList)
                    {
                        Console.WriteLine("Duplicate argument: --ant specified more than once");
                        Usage();
                    }
                    antennaList = ParseAntennaList(args, nextarg);
                    nextarg++;
                }
                else
                {
                    Console.WriteLine("Argument {0}:\"{1}\" is not recognized", nextarg, arg);
                    Usage();
                }
            }

            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automatic
                // reader shutdown (using IDisposable interface).
                using (r = Reader.Create(args[0]))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

                    r.Connect();
                    if (Reader.Region.UNSPEC == (Reader.Region)r.ParamGet("/reader/region/id"))
                    {
                        Reader.Region[] supportedRegions = (Reader.Region[])r.ParamGet("/reader/region/supportedRegions");
                        if (supportedRegions.Length < 1)
                        {
                            throw new FAULT_INVALID_REGION_Exception();
                        }
                        r.ParamSet("/reader/region/id", supportedRegions[0]);
                    }
                    if (r.isAntDetectEnabled(antennaList))
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }

                    //Use first antenna for operation
                    if (antennaList != null)
                        r.ParamSet("/reader/tagop/antenna", antennaList[0]);

                    if (enableFilter)
                    {
                        // If select filter is to be enabled
                        byte[] mask = new byte[]{(byte)0x30,(byte)0x28,(byte)0x35,(byte)0x4D,
                            (byte)0x82,(byte)0x02,(byte)0x02,(byte)0x80,(byte)0x00,(byte)0x01,
                            (byte)0x04,(byte)0xAC};
                        filter = new Gen2.Select(false, Gen2.Bank.EPC, 32, 96, mask);
                    }

                    //Get Sensor Data of EM4325 tag
                    bool sendUid = true;
                    bool sendNewSample = true;
                    Gen2.EMMicro.EM4325.GetSensorData getSensorOp = new Gen2.EMMicro.EM4325.GetSensorData(sendUid, sendNewSample);
                    try
                    {
                        Console.WriteLine("****Executing standalone tag operation of Get sensor Data command of EM4325 tag****");
                        byte[] response = (byte[])r.ExecuteTagOp(getSensorOp, filter);
                        
                        // Parse the response of GetSensorData
                        Console.WriteLine("****Get sensor Data command is success****");
                        GetSensorDataResponse rData = new GetSensorDataResponse(response);
                        Console.WriteLine(rData.ToString());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception from executing get Sensor Data command: " + ex.Message);
                    }

                    //Embedded tag operation of Get sensor data
                    try
                    {
                        Console.WriteLine("****Executing embedded tag operation of Get sensor Data command of EM4325 tag****");
                        embeddedRead(TagProtocol.GEN2, filter, getSensorOp);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception from embedded get sensor data: " + ex.Message);
                    }

                    //Reset alarms command
                    // Read back the temperature control word at 0xEC address to verify reset enable alarm bit is set before executing reset alarm tag op.
                    Console.WriteLine("Reading Temperature control word 1 before resetting alarms to ensure reset enable bit is set to 1");
                    byte[] filterMask = new byte[] { (byte)0x04, (byte)0xc2 };
                    Gen2.Select select = new Gen2.Select(false, Gen2.Bank.EPC, 0x70, 16, filterMask);
                    Gen2.ReadData rdata = new Gen2.ReadData(Gen2.Bank.USER, 0xEC, (byte)0x1);
                    ushort[] resp = (ushort[])r.ExecuteTagOp(rdata, select);
                    Console.WriteLine("Temp control word 1: ");
                    foreach (ushort i in resp)
                    {
                        Console.Write(" {0:X4}", i);
                    }
                    Console.WriteLine("\n");

                    // If temperature control word is not 0x4000, write the data
                    if (resp[0] != 0x4000)
                    {
                        ushort[] writeData = new ushort[] { 0x4000 };
                        Gen2.WriteData wData = new Gen2.WriteData(Gen2.Bank.USER, 0xEC, writeData);
                        r.ExecuteTagOp(wData, select);
                    }
                    Gen2.EMMicro.EM4325.ResetAlarms resetAlarmsOp = new Gen2.EMMicro.EM4325.ResetAlarms();
                    try
                    {
                        Console.WriteLine("****Executing standalone tag operation of Reset alarms command of EM4325 tag****");
                        r.ExecuteTagOp(resetAlarmsOp, filter);
                        Console.WriteLine("****Reset Alarms command is success****");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception from executing reset alarms : " + ex.Message);
                    }

                    //Embedded tag operation of reset alarms command
                    try
                    {
                        Console.WriteLine("****Executing embedded tag operation of reset alarms command of EM4325 tag****");
                        embeddedRead(TagProtocol.GEN2, filter, resetAlarmsOp);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception from embedded reset alarms command: " + ex.Message);
                    }
                }
            }
            catch (ReaderException re)
            {
                Console.WriteLine("Error: " + re.Message);
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        #region ParseAntennaList

        private static int[] ParseAntennaList(IList<string> args, int argPosition)
        {
            int[] antennaList = null;
            try
            {
                string str = args[argPosition + 1];
                antennaList = Array.ConvertAll<string, int>(str.Split(','), int.Parse);
                if (antennaList.Length == 0)
                {
                    antennaList = null;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Missing argument after args[{0:d}] \"{1}\"", argPosition, args[argPosition]);
                Usage();
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0}\"{1}\"", ex.Message, args[argPosition + 1]);
                Usage();
            }
            return antennaList;
        }

        #endregion

        #region EmbeddedRead
        public static void embeddedRead(TagProtocol protocol, TagFilter filter, TagOp tagop)
        {
            TagReadData[] tagReads = null;
            SimpleReadPlan plan = new SimpleReadPlan(antennaList, protocol, filter, tagop, 1000);
            r.ParamSet("/reader/read/plan", plan);
            tagReads = r.Read(1000);
            // Print tag reads
            foreach (TagReadData tr in tagReads)
            {
                Console.WriteLine(tr.ToString());
                if (tr.isErrorData)
                {
                    // In case of error, show the error to user. Extract error code.
                    int errorCode = ByteConv.ToU16(tr.Data, 0);
                    Console.WriteLine("Embedded Tag operation failed. Error: " + ReaderCodeException.faultCodeToMessage(errorCode));
                }
                else
                {
                    if (tagop is Gen2.EMMicro.EM4325.GetSensorData)
                    {
                        if (tr.Data.Length > 0)
                        {
                            GetSensorDataResponse rData = new GetSensorDataResponse(tr.Data);
                            Console.WriteLine("Data:" + rData.ToString());
                        }
                    }
                    else
                    {
                        Console.WriteLine("  Data:" + ByteFormat.ToHex(tr.Data, "", " "));
                    }
                }
            }
        }
        #endregion

        #region GetSensorDataResponse
        public class GetSensorDataResponse
        {
            #region Fields
            /// <summary>
            /// uid of tag
            /// </summary>
            byte[] uid;
            /// <summary>
            /// Sensor data
            /// </summary>
            SensorData sensorData;
            /// <summary>
            /// UTC Timestamp
            /// </summary>
            UInt32 utcTimestamp;
            #endregion

            /// <summary>
            /// GetSensorDataResponse - parses the response and fills uid , sensor data and utc timestamp with values
            /// </summary>
            /// <param name="response">response</param>
            public GetSensorDataResponse(byte[] response)
            {

                // Get sensor data response contains
                // UIDlength(2 bytes) + UID(8 or 10 or 12 bytes) + Sensor Data(4 bytes) + UTC Timestamp(4 bytes) 
                
                // read index
                int readIndex = 0;

                // uid length in bytes
                int uidLen = 0;

                // Extract 2 bytes of dataLength. Datalength includes length of uid + sensorData + UTCTimestamp
                byte[] dataLenBits = new byte[2];
                Array.Copy(response, readIndex, dataLenBits, 0, 2);
                int offset = 0;

                //converts byte array to int value
                int dataLength = ((dataLenBits[offset] & 0xff) << 8) | ((dataLenBits[offset + 1] & 0xff) << 0);
                // dataLength is in bits, so divide by 8 to get overall length in bytes and
                //  Subtract 4(Sensor Data) + 4(UTC timestamp) = 8 bytes to get uid Length
                uidLen = (dataLength / 8) - 8;

                this.uid = getUID(response, readIndex, uidLen);
                this.sensorData = getSensorData(response, readIndex, uidLen);
                this.utcTimestamp = getUTCTimestamp(response, readIndex, uidLen);
            }

            /// <summary>
            /// getUID - retrieves the uid from the response
            /// </summary>
            public byte[] getUID(byte[] response, int readIndex, int uidLen)
            {
                uid = new byte[uidLen];

                // Extract uid of tag if uidLen > 0
                if (uidLen > 0)
                {
                    // Now extract uid based on the length
                    Array.Copy(response, (readIndex + 2), uid, 0, (uidLen));
                }
                return uid;
            }

            /// <summary>
            /// getSensorData - retrieves the sensor data from the response
            /// </summary>
            public SensorData getSensorData(byte[] response, int readIndex, int uidLen)
            {
                // Extract sensor data(4 bytes)
                byte[] sensorDataArray = new byte[4];
                if (uidLen > 0)
                {
                    readIndex = uidLen + 2;// exclude uidLength(2 bytes) and uid bits(uidLen bytes)
                }
                else
                {
                    readIndex = 0;
                }
                Array.Copy(response, readIndex, sensorDataArray, 0, 4);
                UInt32 sData = ByteConv.ToU32(sensorDataArray, 0);
                return new SensorData(sData);
            }

            /// <summary>
            /// getUTCTimestamp - retrieves the UTC timestamp from the response
            /// </summary>
            public UInt32 getUTCTimestamp(byte[] response, int readIndex, int uidLen)
            {
                if (uidLen > 0)
                {
                    readIndex = uidLen + 2 + 4; // exclude uidLength(2 bytes), uidBits(uidLen bytes) and sensorData(4 bytes) 
                }
                else
                {
                    readIndex = 4; //exclude sensor data
                }
                // Extract utc timestamp(4 bytes)
                byte[] utcTimeArray = new byte[4];
                Array.Copy(response, readIndex, utcTimeArray, 0, 4);
                return ByteConv.ToU32(utcTimeArray, 0);;
            }

            /// <summary>
            /// Human-readable representation
            /// </summary>
            /// <returns>Human-readable representation</returns>
            public override string ToString()
            {
                return String.Join(" ", new string[] {
                            "\n UID : " + ByteFormat.ToHex(uid),
                            "\n SensorData : " + sensorData.ToString(),
                            "\n UTCTimestamp=" + utcTimestamp,
                        });
            }
        }
        #endregion

        #region SensorData
        public class SensorData
        {
            #region Enums
            /// <summary>
            /// LowBatteryAlarm
            /// </summary>
            public enum LowBatteryAlarm
            {
                NOPROBLEM = 0,
                LOWBATTERYDETECTED = 1
            }

            /// <summary>
            /// AuxAlarm
            /// </summary>
            public enum AuxAlarm
            {
                NOPROBLEM = 0,
                TAMPER_OR_SPI_ALARM_DETECTED = 1
            }

            /// <summary>
            /// OverTempAlarm
            /// </summary>
            public enum OverTempAlarm
            {
                NOPROBLEM = 0,
                OVERTEMPERATURE_DETECTED = 1
            }

            /// <summary>
            /// UnderTempAlarm
            /// </summary>
            public enum UnderTempAlarm
            {
                NOPROBLEM = 0,
                UNDERTEMPERATURE_DETECTED = 1
            }

            /// <summary>
            /// P3Input
            /// </summary>
            public enum P3Input
            {
                NOSIGNAL = 0,
                SIGNALLEVEL = 1
            }

            /// <summary>
            /// MonitorEnabled
            /// </summary>
            public enum MonitorEnabled
            {
                DISABLED = 0,
                ENABLED = 1
            }
            #endregion

            #region Fields
            
            /// <summary>
            /// LowBatteryAlarm status- MSW Bit 0
            /// </summary>
            LowBatteryAlarm lowBatteryAlarmStatus;
            /// <summary>
            /// AuxAlarm status - MSW Bit 1
            /// </summary>
            AuxAlarm auxAlarmStatus;
            /// <summary>
            /// OverTempAlarm status - MSW Bit 2
            /// </summary>
            OverTempAlarm overTempAlarmStatus;
            /// <summary>
            /// UnderTempAlarm status - MSW Bit 3
            /// </summary>
            UnderTempAlarm underTempAlarmStatus;
            /// <summary>
            /// P3Input status - MSW Bit 4
            /// </summary>
            P3Input p3InputStatus;
            /// <summary>
            /// MonitorEnabled status - MSW Bit 5
            /// </summary>
            MonitorEnabled monitorEnabledStatus;
            //MSW Bit 6 always 0.
            /// <summary>
            /// Temperature value in degree celsius -  MSW Bit 7 - F (9 bits)
            /// </summary>
            double temperature;
            /// <summary>
            /// Aborted Temperature Count - LSW Bits 0 - 5
            /// </summary>
            byte abortedTemperatureCount;
            /// <summary>
            /// Under Temperature Count - LSW Bits 6 - A
            /// </summary>
            byte underTemperatureCount;
            /// <summary>
            /// Over Temperature Count  - LSW Bits B - F
            /// </summary>
            byte overTemperatureCount;
            #endregion

            public SensorData(UInt32 sensorData)
            {
                //16 bits of MSW + 16 bits of LSW
                UInt16 sensorDataRplyMSW = (UInt16)((sensorData & 0xFFFF0000) >> 16);
                UInt16 sensorDataRplyLSW = (UInt16)(sensorData & 0xFFFF);

                //MSW parsing
                lowBatteryAlarmStatus = getLowBatteryAlarmStatus(sensorDataRplyMSW);
                auxAlarmStatus = getAuxAlarmStatus(sensorDataRplyMSW);
                overTempAlarmStatus = getOverTempAlarmStatus(sensorDataRplyMSW);
                underTempAlarmStatus = getUnderTempAlarmStatus(sensorDataRplyMSW);
                p3InputStatus = getP3InputStatus(sensorDataRplyMSW);
                monitorEnabledStatus = getMonitorEnabledStatus(sensorDataRplyMSW);
                temperature = getTemperature(sensorDataRplyMSW);

                //LSW parsing
                abortedTemperatureCount = getAbortedTemperatureCount(sensorDataRplyLSW);
                underTemperatureCount = getUnderTemperatureCount(sensorDataRplyLSW);
                overTemperatureCount = getOverTemperatureCount(sensorDataRplyLSW);
            }

            //MSW Bit 0
            public LowBatteryAlarm getLowBatteryAlarmStatus(UInt16 sensorDataRplyMSW)
            {
                return (LowBatteryAlarm)(sensorDataRplyMSW >> 15);
            }
            //MSW Bit 1
            public AuxAlarm getAuxAlarmStatus(UInt16 sensorDataRplyMSW)
            {
                return (AuxAlarm)(sensorDataRplyMSW >> 14);
            }
            //MSW Bit 2
            public OverTempAlarm getOverTempAlarmStatus(UInt16 sensorDataRplyMSW)
            {
                return (OverTempAlarm)(sensorDataRplyMSW >> 13);
            }
            //MSW Bit 3
            public UnderTempAlarm getUnderTempAlarmStatus(UInt16 sensorDataRplyMSW)
            {
                return (UnderTempAlarm)(sensorDataRplyMSW >> 12);
            }
            //MSW Bit 4
            public P3Input getP3InputStatus(UInt16 sensorDataRplyMSW)
            {
                return (P3Input)(sensorDataRplyMSW >> 11);
            }
            //MSW Bit 5
            public MonitorEnabled getMonitorEnabledStatus(UInt16 sensorDataRplyMSW)
            {
                return (MonitorEnabled)(sensorDataRplyMSW >> 10);
            }
            //MSW Bit 6 always 0.
            //MSW Bit 7 - F (9 bits) for Temperature
            public double getTemperature(UInt16 sensorDataRplyMSW)
            {
                double temp = (double)((sensorDataRplyMSW & 0x1ff) * 0.25);
                return temp;
            }
            //LSW Bits 0 - 5 
            public byte getAbortedTemperatureCount(UInt16 sensorDataRplyLSW)
            {
                return (byte)((sensorDataRplyLSW >> 10) & 0xFC);
            }
            //LSW Bits 6 - A
            public byte getUnderTemperatureCount(UInt16 sensorDataRplyLSW)
            {
                return (byte)((sensorDataRplyLSW >> 5) & 0x1F);
            }
            //LSW Bits B - F
            public byte getOverTemperatureCount(UInt16 sensorDataRplyLSW)
            {
                return (byte)((sensorDataRplyLSW >> 0) & 0x1F);
            }
            /// <summary>
            /// Human-readable representation
            /// </summary>
            /// <returns>Human-readable representation</returns>
            public override string ToString()
            {
                return String.Join(" ", new string[] {
                            "lowBatteryAlarmStatus = " + lowBatteryAlarmStatus,
                            ", auxAlarmStatus = " + auxAlarmStatus,
                            ", overTempAlarmStatus = " + overTempAlarmStatus,
                            ", underTempAlarmStatus = " + underTempAlarmStatus,
                            ", p3InputStatus = " + p3InputStatus,
                            ", monitorEnabledStatus = " + monitorEnabledStatus,
                            String.Format(", temperature = {0} C", temperature),
                            ", abortedTemperatureCount = " + abortedTemperatureCount,
                            ", underTemperatureCount = " + underTemperatureCount,
                            ", overTemperatureCount = " + overTemperatureCount,

                        });
            }
        }
        #endregion
    }
}