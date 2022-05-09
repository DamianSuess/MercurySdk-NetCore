using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;
using System.IO.Ports;


namespace AutonomousMode
{
    /// <summary>
    /// Sample program that demonstrates enable/disable AutonomousMode
    /// </summary>
    class AutonomousMode
    {
        public static string configOption = null;
        public static string autoReadType = null;
        public static string modelID = null;
        public static int trigTypeNum;
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [reader-uri] [--ant n[,n...]] --config option --trigger pinNum",
                    "reader-uri  Reader URI: e.g., \"tmr:///COM1\", \"tmr://astra-2100d3\"\n",
                 "--ant  Antenna List: e.g., \"--ant 1\", \"--ant 1,2\"\n",
                  "--config option: Indicates configuration options of the reader \n",
                "                   options: 1 - saveAndRead\n",
                "                            2 - save\n ",
                "                            3 - stream\n ",
                "                            4 - verify\n ",
                "                            5 - clear\n ",
                " , e.g., --config 1 for saving and enabling autonomous read\n",
                "[--trigger pinNum] e.g., --trigger 0 for auto read on boot, --trigger 1 for read on gpi pin 1\n ",
                "[--model option] : model indicates model of the reader\n",
                "                   option : 1 - UHF Reader\n ",
                "                            2 - M3e reader\n",
                "Example for UHF: tmr:///com1 --ant 1,2 --config 1 --trigger 0 for autonomous read on boot\n ",
                "                 tmr:///com1 --ant 1,2 --config 1 --trigger 1 for gpi trigger read on pin 1\n ",
                "                 tmr:///com1 --ant 1,2 --config 2, tmr:///com1 --ant 1,2 --config 3\n ",
                "                 tmr:///com1 --ant 1,2 --config 2, tmr:///com1 --ant 1,2 --config 3 --model 1\n",
                "Example for HF/LF: tmr:///com1 --config 1 --trigger 0\n",
                "                   tmr:///com1 --config 3 --model 2\n"
            }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            // Program setup
            if (1 > args.Length)
            {
                Usage();
            }
            int[] antennaList = null;
            string AutonomousMode = string.Empty;
            Boolean isModelOptionConfigured = false;
            Boolean enableAutoRead = true;
            SimpleReadPlan srp;

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
                else if (arg.Equals("--config"))
                {
                    string option = args[++nextarg];
                    switch (option)
                    {
                        case "1":
                            // Saves the configuration and performs read with the saved configuration
                            configOption = "saveAndRead";
                            break;
                        case "2":
                            // Only saves the configuration
                            configOption = "save";
                            break;
                        case "3":
                            // Streams the tag responses if autonomous read is already enabled
                            configOption = "stream";
                            break;
                        case "4":
                            //Verifies the current config as per saved one
                            configOption = "verify";
                            break;
                        case "5":
                            //Clears the configuration
                            configOption = "clear";
                            break;
                        default:
                            Console.WriteLine("Please select config option between 1 and 5");
                            Usage();
                            break;
                    }
                }
                else if (arg.Equals("--trigger"))
                {
                    string triType = args[++nextarg];
                    trigTypeNum = Convert.ToInt32(triType);
                    switch (triType)
                    {
                        case "0":
                            autoReadType = "ReadOnBoot";
                            break;
                        case "1":
                        case "2":
                        case "3":
                        case "4":
                            autoReadType = "ReadOnGPI";
                            break;
                        default:
                            Console.WriteLine("Please select trigger option between 1 and 4");
                            Usage();
                            break;
                    }
                }
                else if (arg.Equals("--model"))
                {
                    string model = args[++nextarg];
                    switch (model)
                    {
                        case "1":
                        case "2":
                            modelID = model;
                            isModelOptionConfigured = true;
                            break;
                        default:
                            Console.WriteLine("Please select model option between 1 and 2\n");
                            Usage();
                            break;
                    }
                }
                else if (arg.Equals("--enable"))
                {
                    if (AutonomousMode != String.Empty)
                    {
                        Console.WriteLine("Duplicate argument: --enable specified more than once");
                        Usage();
                    }
                    AutonomousMode = "true";
                }
                else if (arg.Equals("--disable"))
                {
                    if (AutonomousMode != String.Empty)
                    {
                        Console.WriteLine("Duplicate argument: --disable specified more than once");
                        Usage();
                    }
                    AutonomousMode = "false";
                }
                else
                {
                    Console.WriteLine("Argument {0}:\"{1}\" is not recognized", nextarg, arg);
                    Usage();
                }
            }

            if (((autoReadType != null) && ((autoReadType == "ReadOnBoot") || (autoReadType == "ReadOnGPI"))) && !(configOption == "saveAndRead"))
            {
                Console.WriteLine("Please select saveAndRead config option to enable autoReadType");
                Usage();
            }

            //model option is supported only with "stream" configuration, throw error if user sets model option with other config options like saveAndRead, save, clear, verify
            if ((isModelOptionConfigured) && !(configOption == "stream"))
            {
                Console.WriteLine("Please select model with config option 3 only");
                Usage();
            }

            //--model option is mandatory for "stream" option.
            if (!(isModelOptionConfigured) && (configOption == "stream"))
            {
                Console.WriteLine("Model is a mandatory field for config option 3. Please provide model.");
                Usage();
            }

            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automaticq
                // reader shutdown (using IDisposable interface).
                using (Reader r = Reader.Create(args[0]))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

                    if (configOption == "stream")
                    {
                        // Initialize the params
                        ((SerialReader)r).autonomousStreaming = true;
                        ((SerialReader)r)._StatFlag = Reader.Stat.StatsFlag.TEMPERATURE;
                        if (modelID == "2")
                        {
                            ((SerialReader)r).model = "M3e";
                        }

                        //stream option will open the serial port and try to receive the autonomous responses
                        SerialConnect sc = new SerialConnect();
                        Boolean isConnected = sc.Connect(args[0], r);
                        if (isConnected)
                        {
                            #region Create and add listeners

                            // Create and add tag listener
                            r.TagRead += new EventHandler<TagReadDataEventArgs>(r_tagRead);

                            // Add reader stats listener
                            r.StatsListener += r_StatsListener;

                            // Create and add read exception listener
                            r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);

                            #endregion

                            r.ReceiveAutonomousReading();
                            while (true)
                            {
                                Thread.Sleep(5000);
                            }
                        }
                    }
                    else
                    {
                        r.Connect();
                        string model = (string)r.ParamGet("/reader/version/model");
                        if ("M6e".Equals(model) || "M6e PRC".Equals(model) || "M6e JIC".Equals(model) || "M6e Micro".Equals(model) ||
                            "M6e Nano".Equals(model) || "M6e Micro USB".Equals(model) || "M6e Micro USBPro".Equals(model) || "M3e".Equals(model))
                        {
                            if (!model.Equals("M3e"))
                            {
                                if ((model.Equals("M6e Micro") || model.Equals("M6e Nano")) && antennaList == null)
                                {
                                    Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                                    Usage();
                                }
                            }
                            else
                            {
                                if (antennaList != null)
                                {
                                    Console.WriteLine("Module doesn't support antenna input");
                                    Usage();
                                }
                            }

                            if (configOption == "saveAndRead")
                            {
                                if ("M3e".Equals(model))
                                {
                                    srp = configureM3ePersistentSettings(r, antennaList);
                                }
                                else
                                {
                                    srp = configureUHFPersistentSettings(r, model, antennaList);
                                }
                                //To disable autonomous read make enableAutonomousRead flag to false and do SAVEWITHRREADPLAN 
                                srp.enableAutonomousRead = enableAutoRead;
                                r.ParamSet("/reader/read/plan", srp);

                                //Uncomment this if readerstats need to be included
                                r.ParamSet("/reader/stats/enable", Reader.Stat.StatsFlag.TEMPERATURE);

                                r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.SAVEWITHREADPLAN));
                                Console.WriteLine("User profile set option:save all configuration with read plan is successfull");
                                try
                                {
                                    r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.RESTORE));
                                    Console.WriteLine("User profile set option:restore all configuration is successfull");
                                }
                                catch (ReaderException ex)
                                {
                                    if (ex.Message.Contains("Verifying flash contents failed"))
                                    {
                                        Console.WriteLine("Please use saveAndRead option to trigger autonomous read");
                                    }
                                }
                                if (enableAutoRead)
                                {
                                    #region Create and add listeners

                                    // Create and add tag listener
                                    r.TagRead += new EventHandler<TagReadDataEventArgs>(r_tagRead);

                                    // Add reader stats listener
                                    r.StatsListener += r_StatsListener;

                                    // Create and add read exception listener
                                    r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);

                                    #endregion Create and add listeners

                                    r.ReceiveAutonomousReading();
                                    //receive the tags for 5 secs
                                    Thread.Sleep(5000);
                                }
                            }
                            else if (configOption == "save")
                            {
                                if (model.Equals("M3e"))
                                {
                                    srp = configureM3ePersistentSettings(r, antennaList);
                                }
                                else
                                {
                                    srp = configureUHFPersistentSettings(r, model, antennaList);
                                }

                                //To disable autonomous read make enableAutonomousRead flag to false and do SAVEWITHRREADPLAN 
                                srp.enableAutonomousRead = enableAutoRead;
                                r.ParamSet("/reader/read/plan", srp);
                                r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.SAVEWITHREADPLAN));
                                Console.WriteLine("User profile set option:save all configuration with read plan is successfull");
                            }
                            else if (configOption == "verify")
                            {
                                r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.VERIFY));
                                Console.WriteLine("User profile set option:verify all configuration is successful");
                            }
                            else if (configOption == "clear")
                            {
                                r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.CLEAR));
                                Console.WriteLine("User profile set option:clear all configuration is successful");
                            }
                            else
                            {
                                throw new Exception("Please input correct config option");
                            }

                        }
                        else
                        {
                            Console.WriteLine("Error: This codelet works only on M3e and M6e variants");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
        static void r_StatsListener(object sender, StatsReportEventArgs e)
        {
            Console.WriteLine(e.StatsReport.ToString());
            Console.WriteLine();
        }

        private static void r_ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Console.WriteLine("Error: " + e.ReaderException.Message);
        }

        private static void r_tagRead(object sender, TagReadDataEventArgs e)
        {
            Console.WriteLine("Background read: " + e.TagReadData);
            if (0 < e.TagReadData.Data.Length)
            {
                if (e.TagReadData.isErrorData)
                {
                    // In case of error, show the error to user. Extract error code.
                    byte[] errorCodeBytes = e.TagReadData.Data;
                    int offset = 0;
                    //converts byte array to int value
                    int errorCode = (((errorCodeBytes[offset] & 0xFF) << 8) | ((errorCodeBytes[offset + 1] & 0xFF) << 0));
                    Console.WriteLine("Embedded Tag operation failed. Error: " + ReaderCodeException.faultCodeToMessage(errorCode));
                }
                else
                {
                    Console.WriteLine("Data[" + e.TagReadData.dataLength + "]: " + ByteFormat.ToHex(e.TagReadData.Data, "", " "));
                }
            }
        }

        #region Configure UHF Persistent Setting
        private static SimpleReadPlan configureUHFPersistentSettings(Reader r, string model, int[] antennaList)
        {
            // baudrate            
            r.ParamSet("/reader/baudRate", 115200);

            // Region
            Reader.Region[] supportRegion = (Reader.Region[])r.ParamGet("/reader/region/supportedRegions");
            if (supportRegion.Length < 1)
            {
                throw new Exception("Reader doesn't support any regions");
            }
            else
            {
                r.ParamSet("/reader/region/id", supportRegion[0]);
            }

            // Protocol
            TagProtocol protocol = TagProtocol.GEN2;
            r.ParamSet("/reader/tagop/protocol", protocol);

            // Gen2 setting
            r.ParamSet("/reader/gen2/BLF", Gen2.LinkFrequency.LINK250KHZ);
            r.ParamSet("/reader/gen2/tari", Gen2.Tari.TARI_25US);
            r.ParamSet("/reader/gen2/target", Gen2.Target.A);
            r.ParamSet("/reader/gen2/tagEncoding", Gen2.TagEncoding.M2);
            r.ParamSet("/reader/gen2/session", Gen2.Session.S0);
            r.ParamSet("/reader/gen2/q", new Gen2.DynamicQ());

            //RF Power settings
            r.ParamSet("/reader/radio/readPower", 2000);
            r.ParamSet("/reader/radio/writePower", 2000);

            // Hop Table
            int[] hopTable = (int[])r.ParamGet("/reader/region/hopTable");
            r.ParamSet("/reader/region/hopTable", hopTable);

            int hopTimeValue = (int)r.ParamGet("/reader/region/hopTime");
            r.ParamSet("/reader/region/hopTime", hopTimeValue);

            //For Open region, dwell time, minimum frequency, quantization step can also be configured persistently
            if (Reader.Region.OPEN == (Reader.Region)r.ParamGet("/reader/region/id"))
            {
                //Set dwell time enable before stting dwell time
                r.ParamSet("/reader/region/dwellTime/enable", true);
                //set quantization step
                r.ParamSet("/reader/region/quantizationStep", 25000);

                //set dwell time
                r.ParamSet("/reader/region/dwellTime", 250);

                //set minimum frequency
                r.ParamSet("/reader/region/minimumFrequency", 859000);
            }

            // Embedded tag operation
            /* Uncomment the following line if want to enable embedded read with autonomous operation.
             * Add tagop object op in simple read plan constructor.
             * Add filter object epcFilter in simple read plan constructor.
             */
            //Gen2.ReadData readtagOp = new Gen2.ReadData(Gen2.Bank.RESERVED, 2, 2);
            //Gen2.TagData epcFilter = new Gen2.TagData(new byte[] { 0x11,0x22,0x33,0x44,0x55,0x66,0x77,0x88,0x99,0x00,0x99,0x99});

            GpiPinTrigger gpiTrigger = null;
            if ((autoReadType != null) && (autoReadType == "ReadOnGPI"))
            {
                gpiTrigger = new GpiPinTrigger();
                //Gpi trigger option not there for M6e Micro USB
                if (!("M6e Micro USB".Equals(model)))
                {
                    gpiTrigger.enable = true;
                    //set the gpi pin to read on
                    r.ParamSet("/reader/read/trigger/gpi", new int[] { trigTypeNum });
                }
            }
            SimpleReadPlan srp = new SimpleReadPlan(antennaList, protocol, null, null, 1000);
            if ((autoReadType != null) && autoReadType == "ReadOnGPI")
            {
                if (!("M6e Micro USB".Equals(model)))
                {
                    srp.ReadTrigger = gpiTrigger;
                }
            }
            return srp;
        }
        #endregion

        #region Configure M3e persistant setting
        private static SimpleReadPlan configureM3ePersistentSettings(Reader r, int[] antennaList)
        {
            try
            {
                //baudrate
                r.ParamSet("/reader/baudRate", 115200);
                //Protocol
                TagProtocol protocol = TagProtocol.ISO14443A;
                r.ParamSet("/reader/tagop/protocol", protocol);
                //enable read filter
                r.ParamSet("/reader/tagReadData/enableReadFilter", true);

                // Embedded tag operation
                /* Uncomment the following line if want to enable embedded read with autonomous operation.
                 * Add tagop object op in simple read plan constructor.
                 * Add filter object epcFilter in simple read plan constructor.
                 */
                //MemoryType type = MemoryType.BLOCK_MEMORY;
                //int address = 0;
                //byte length = 1;
                //ReadMemory tagOp = new ReadMemory(type, address, length);
                //Select_UID filter = new Select_UID(32, new byte[] { 0x11, 0x22, 0x33, 0x44 });

                GpiPinTrigger gpiTrigger = null;
                if ((autoReadType != null) && (autoReadType == "ReadOnGPI"))
                {
                    gpiTrigger = new GpiPinTrigger();
                    gpiTrigger.enable = true;
                    //set the gpi pin to read on
                    r.ParamSet("/reader/read/trigger/gpi", new int[] { trigTypeNum });
                }
                SimpleReadPlan srp = new SimpleReadPlan(antennaList, protocol, null, null, 1000);
                if ((autoReadType != null) && (autoReadType == "ReadOnGPI"))
                {
                    srp.ReadTrigger = gpiTrigger;
                }
                return srp;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        #endregion

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
    }
    class SerialConnect
    {
        private SerialTransport _serialPort = new SerialTransportNative();
        private DateTime baseTime;
        #region Connect

        public Boolean Connect(string deviceName, Reader r)
        {
            bool isConnected = false;
            try
            {
                string portNames = Regex.Replace(deviceName, @"[^0-9_\\]", "").ToUpperInvariant();
                _serialPort.PortName = "COM" + portNames;
                Console.WriteLine("Waiting for streaming...");
                baseTime = DateTime.MinValue;
                while (!isConnected)
                {
                    _serialPort.WriteTimeout = 1000;
                    _serialPort.ReadTimeout = 1000;
                    int[] bps = { 115200, 9600, 921600, 19200, 38400, 57600, 230400, 460800 };
                    foreach (int baudRate in bps)
                    {
                        try
                        {
                            _serialPort.BaudRate = baudRate;
                            _serialPort.Open();
                            byte[] response = new byte[256];
                            SerialReader serialReader = r as SerialReader;
                            serialReader.receiveResponse(_serialPort, "");
                        }
                        catch (Exception ex)
                        {
                            //_serialPort.Close();
                            if (ex.Message.Contains("is denied"))
                            {
                                throw ex;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        isConnected = true;
                        break;
                    }
                }
                Console.WriteLine("Connection to the module is successful");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return isConnected;
        }

        #endregion
    }
}