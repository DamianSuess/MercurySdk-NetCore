using System;
using System.Collections.Generic;
using System.Text;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;

namespace SavedReadPlanConfig
{
    class SavedReadPlanConfig
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example for UHF: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'",
                    " Example for HF/LF: 'tmr:///com4'"
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
                // Wrap reader in a "using" block to get automaticq
                // reader shutdown (using IDisposable interface).
                using (Reader r = Reader.Create(args[0]))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

                    r.Connect();
                    string model = (string)r.ParamGet("/reader/version/model");
                    if ("M6e".Equals(model) || "M6e PRC".Equals(model) || "M6e JIC".Equals(model) || "M6e Micro".Equals(model) ||
                        "M6e Nano".Equals(model) || "M6e Micro USB".Equals(model) || "M6e Micro USBPro".Equals(model) || "M3e".Equals(model))
                    {
                        if (Reader.Region.UNSPEC == (Reader.Region)r.ParamGet("/reader/region/id"))
                        {
                            Reader.Region[] supportedRegions = (Reader.Region[])r.ParamGet("/reader/region/supportedRegions");
                            if (supportedRegions.Length < 1)
                            {
                                throw new FAULT_INVALID_REGION_Exception();
                            }
                            r.ParamSet("/reader/region/id", supportedRegions[0]);
                        }
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

                        //Uncomment the following line to revert the module settings to factory defaluts
                        //r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.CLEAR));
                        //Console.WriteLine("User profile set option:reset all configuration");

                        //Uncomment this if readerstats need to be included
                        //r.ParamSet("/reader/stats/enable", Reader.Stat.StatsFlag.TEMPERATURE);

                        /* Uncomment the following lines if want to enable embedded read and filter with autonomous operation.
                         * Add tagop object readtagOp and filter object epcFilter in simple read plan constructor.
                         */

                        //if (!model.Equals("M3e"))
                        //{
                            //Gen2.ReadData readtagOp = new Gen2.ReadData(Gen2.Bank.RESERVED, 2, 2);
                            //Gen2.TagData epcFilter = new Gen2.TagData(new byte[] { 0x11,0x22,0x33,0x44,0x55,0x66,0x77,
                            //0x88,0x99,0x00,0x99,0x99});
                        //}
                        int[] gpiPin = new int[1];
                        gpiPin[0] = 1;
                        r.ParamSet("/reader/read/trigger/gpi", gpiPin);
                        GpiPinTrigger gpiPinTrigger = new GpiPinTrigger();
                        gpiPinTrigger.enable = true;
                        SimpleReadPlan srp;
                        if (model.Equals("M3e"))
                        {
                            // initializing the simple read plan 
                            srp = new SimpleReadPlan(antennaList, TagProtocol.ISO14443A, null, null, 1000);
                        }
                        else
                        {
                            srp = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, null, 1000);
                        }
                        //To disable autonomous read make enableAutonomousRead flag to false and do SAVEWITHRREADPLAN
                        srp.enableAutonomousRead = true;
                        srp.ReadTrigger = gpiPinTrigger;

                        r.ParamSet("/reader/read/plan", srp);
                        r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.SAVEWITHREADPLAN));
                        Console.WriteLine("User profile set option:save all configuration with read plan");

                        r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.RESTORE));
                        Console.WriteLine("User profile set option:restore all configuration");

                        #region Create and add listeners

                        // Create and add tag listener
                        r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                        {
                            Console.WriteLine("Background read: " + e.TagReadData);
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
                        };

                        // Add reader stats listener
                        r.StatsListener += r_StatsListener;

                        #endregion Create and add listeners

                        r.ReceiveAutonomousReading();
                        Thread.Sleep(5000);
                    }
                    else
                    {
                        Console.WriteLine("Error: This codelet works only on M3e and M6e variants");
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
}