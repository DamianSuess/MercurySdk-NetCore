using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;
using System.Threading;

// Sample program that demonstrates different types and uses of TagFilter objects.
namespace CustomAntennaConfig
{
    /// <summary>
    /// Sample program that demonstrates different types and uses of TagFilter objects.
    /// </summary>
    class CustomAntennaConfig
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'"
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

            // Create Reader object, connecting to physical device
            try
            {
                Reader r;
                TagReadData[] filteredTagReads;

                r = Reader.Create(args[0]);

                //Uncomment this line to add default transport listener.
                r.Transport += r.SimpleTransportListener;

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

                ////To perform standalone operations
                if (antennaList != null)
                    r.ParamSet("/reader/tagop/antenna", antennaList[0]);

                try
                {
                    Gen2.Select tidFilter = new Gen2.Select(false, Gen2.Bank.TID, 32, 16, new byte[] { (byte)0x01, (byte)0x2E });
                    tidFilter.target = Gen2.Select.Target.Inventoried_S1;
                    tidFilter.action = Gen2.Select.Action.ON_N_OFF;
                   
                    Gen2.Select epcFilter = new Gen2.Select(false, Gen2.Bank.EPC, 32, 16, new byte[] { (byte)0x11, (byte)0x22 });
                    epcFilter.target = Gen2.Select.Target.Inventoried_S1;
                    epcFilter.action = Gen2.Select.Action.NEG_N_NOP;

                    CustomAntConfigPerAntenna sig = new CustomAntConfigPerAntenna(Gen2.Session.S1, Gen2.Target.A, tidFilter, 1);
                    CustomAntConfigPerAntenna sig2 = new CustomAntConfigPerAntenna(Gen2.Session.S1, Gen2.Target.A, epcFilter, 2);

                    List<CustomAntConfigPerAntenna> CustomConfigAnt = new List<CustomAntConfigPerAntenna>();
                    CustomConfigAnt.Add(sig);
                    CustomConfigAnt.Add(sig2);

                    //parameters in the CustomAntConfig
                    //1. No. of antennas
                    //2. List of CustomAntConfigPerAntenna 
                    //3. Fast search enabled / disabled
                    //4. Dynamic - 1(default)/ Equal
                    //5. Tagreadtimeout
                    CustomAntConfig cnf = new CustomAntConfig(2, CustomConfigAnt, false, 1, 50000);
                    
                    SimpleReadPlan srp = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, cnf);
                    r.ParamSet("/reader/read/plan", srp);
                    ////Sync Read
                    ////
                    //filteredTagReads = r.Read(5000);
                    //foreach (TagReadData tr in filteredTagReads)
                    //    Console.WriteLine(tr.ToString());
                    //Console.WriteLine();

                    ////Async read 
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                   {
                       Console.WriteLine("Background read: " + e.TagReadData);
                   };
                    // Create and add read exception listener
                    r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);

                    // Search for tags in the background
                    r.StartReading();

                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);
                    r.StopReading();

                }
                finally
                {
                }
                // Shut down reader
                r.Destroy();
            }
            catch (ReaderException re)
            {
                Console.WriteLine("Error: " + re.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        private static void r_ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Console.WriteLine("Error: " + e.ReaderException.Message);
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

