using System;
using System.Collections.Generic;
using System.Text;
using ThingMagic;
using System.Threading;

namespace RegulatoryTest
{
    class RegulatoryTest
    {
        static int[] antennaList = null;
        static Reader r = null;
        static bool stopRequested = false;
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]] [--mode regulatory_mode] [--modulation regulatory_modulation] [--ontime regulatory_ontime] [--offtime regulatory_offtime]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",    
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " [--mode regulatory_mode] : e.g., '--mode CONTINUOUS/TIMED'",
                    " [--modulation regulatory_modulation] : e.g., '--mode CW/PRBS'",
                    " [--ontime regulatory_ontime] : e.g., '--ontime 1000'",
                    " [--offtime regulatory_offtime] : e.g., '--offtime 500'",
                    " Example: -v tmr:///com4 --ant 1 --mode CONTINUOUS --modulation CW --ontime 1000 --offtime 500"
            }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            Reader.RegulatoryMode regMode = Reader.RegulatoryMode.TIMED;
            Reader.RegulatoryModulation regModulation = Reader.RegulatoryModulation.CW;
            int regOnTime = 0;
            int regOffTime = 0;
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
                else if (arg.ToLower().Equals("--mode"))
                {
                    String mode = args[nextarg + 1];
                    if (mode.Equals("TIMED"))
                    {
                        regMode = Reader.RegulatoryMode.TIMED;
                    }
                    else if (mode.Equals("CONTINUOUS"))
                    {
                        regMode = Reader.RegulatoryMode.CONTINUOUS;
                    }
                    else
                    {
                        Console.WriteLine("Argument {0} is not recognized. Regulatory mode can be either CONTINUOUS or TIMED.\n\n", mode);
                        Usage();
                    }
                    nextarg++;
                }
                else if (arg.ToLower().Equals("--modulation"))
                {
                    String modulation = args[nextarg + 1];
                    if (modulation.Equals("CW"))
                    {
                        regModulation = Reader.RegulatoryModulation.CW;
                    }
                    else if (modulation.Equals("PRBS"))
                    {
                        regModulation = Reader.RegulatoryModulation.PRBS;
                    }
                    else
                    {
                        Console.WriteLine("Argument {0} is not recognized. Regulatory modulation can be either CW or PRBS.\n\n", modulation);
                        Usage();
                    }
                    nextarg++;
                }
                else if (arg.ToLower().Equals("--ontime"))
                {
                    String onTime = args[nextarg + 1];
                    regOnTime = Convert.ToInt32(onTime);
                    nextarg++;
                }
                else if (arg.ToLower().Equals("--offtime"))
                {
                    String offTime = args[nextarg + 1];
                    regOffTime = Convert.ToInt32(offTime);
                    nextarg++;
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
                    r.ParamSet("/reader/regulatory/mode", regMode);
                    r.ParamSet("/reader/regulatory/modulation", regModulation);
                    r.ParamSet("/reader/regulatory/onTime", regOnTime);
                    r.ParamSet("/reader/regulatory/offTime", regOffTime);
                    r.ParamSet("/reader/commandTimeout", (regOnTime + regOffTime));
                    Console.WriteLine("!!!!! ALERT !!!!");
                    Console.WriteLine("Module may get hot when RF ON time is more than 10 seconds");
                    Console.WriteLine("Risk of damage to the module despite auto cut off feature");
                    if (regMode == Reader.RegulatoryMode.TIMED)
                    {
                        r.ParamSet("/reader/regulatory/enable", true);
                        for (int iterations = 0; iterations < regOnTime / 1000; iterations++)
                        {
                            Thread.Sleep(1000);
                            Console.WriteLine("Temperature: " + r.ParamGet("/reader/radio/temperature"));
                        }
                    }
                    else
                    {
                        ThreadStart threadDelegate = new ThreadStart(RegulatoryTest.DoWork);
                        Thread regulatoryThread = new Thread(threadDelegate);

                        r.ParamSet("/reader/regulatory/enable", true);
                        regulatoryThread.Start();
                        Thread.Sleep(5000); // main thread sleep 
                        stopRequested = true;
                        r.ParamSet("/reader/regulatory/enable", false);
                        
                    }
                }
            }
            catch (ReaderException ex)
            {
                if (ex.Message.Equals("The module has exceeded the maximum or minimum operating temperature "
                    + "and will not allow an RF operation until it is back in range"))
                {
                    try
                    {
                        r.ParamSet("/reader/regulatory/enable", false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
                Console.WriteLine(ex.Message);
                Console.WriteLine("Error: " + ex.Message);
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        public static void DoWork()
        {
            System.Object lockThis = new System.Object();
            lock (lockThis)
            {
                if (stopRequested)
                {
                    Console.WriteLine("Thread stopped: " + stopRequested);
                }

                while (!stopRequested)
                {
                    try
                    {
                        if (r != null)
                        {
                            Console.WriteLine("Temperature: " + r.ParamGet("/reader/radio/temperature"));
                            Thread.Sleep(1000);
                        }
                    }
                    catch (ReaderException re)
                    {
                        if (re.Message.Equals("The module has exceeded the maximum or minimum operating temperature "
                        + "and will not allow an RF operation until it is back in range"))
                        {
                            Console.WriteLine("Reader temperature is too high. Turning OFF RF");
                            try
                            {
                                r.ParamSet("/reader/regulatory/enable", false);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                            }
                        }
                    }
                }
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

    }
}
