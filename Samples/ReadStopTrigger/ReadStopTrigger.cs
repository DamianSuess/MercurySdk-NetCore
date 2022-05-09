//Uncomment this to executes sync read.
#define ENABLE_SYNC_READ
//Uncomment this to executes Async read.
#define ENABLE_ASYNC_READ
//Uncomment this to set simple read plan or Comment this to set multi read plan
//#define ENABLE_SIMPLE_READPLAN
#if !ENABLE_SYNC_READ
//Uncomment this to performs dynamic switching, which is applicable for M3e.
#define ENABLE_DYNAMIC_SWITCHING
#endif


using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace ReadStopTrigger
{
    /// <summary>
    /// Sample program that shows how to create a stoptrigger readplan to reads tags for a n number of tags
    /// and prints the tags found.
    /// </summary>
    class ReadStopTrigger
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
            int totalTagCount = 0;
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
                using (Reader r = Reader.Create(args[0]))
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
                    string model = (string)r.ParamGet("/reader/version/model").ToString();
                    if (!model.Equals("M3e"))
                    {
                        if (r.isAntDetectEnabled(antennaList))
                        {
                            Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                            Usage();
                        }

                        // Set the q value
                        r.ParamSet("/reader/gen2/q", new Gen2.StaticQ(1));
                    }
                    else
                    {
                        if (antennaList != null)
                        {
                            Console.WriteLine("Module doesn't support antenna input");
                            Usage();
                        }
                    }
                    // Set the number of tags to read
                    StopOnTagCount sotc = new StopOnTagCount();
                    sotc.N = 5;
                    StopTriggerReadPlan readplan;
#if ENABLE_SIMPLE_READPLAN
                    if (model.Equals("M3e"))
                    {
                        readplan = new StopTriggerReadPlan(sotc, antennaList, TagProtocol.ISO14443A, null, null, 1000);
                        // Set readplan
                        r.ParamSet("/reader/read/plan", readplan);
                    }
                    else
                    {
                        // Prepare single read plan.
                        readplan = new StopTriggerReadPlan(sotc, antennaList, TagProtocol.GEN2, null, null, 1000);
                        // Set readplan
                        r.ParamSet("/reader/read/plan", readplan);
                    }
#else


                    TagProtocol[] protocolList = (TagProtocol[])r.ParamGet("/reader/version/supportedProtocols");
                    if (model.Equals("M3e"))
                    {
#if ENABLE_DYNAMIC_SWITCHING
                        //Set the multiple protocols using "/reader/protocolList" param for dynamic protocol switching
                        r.ParamSet("/reader/protocolList", protocolList);
                        // If param is set, API ignores the protocol mentioned in readplan.
                        StopTriggerReadPlan plan = new StopTriggerReadPlan(sotc, antennaList, TagProtocol.ISO14443A, null, null, 1000);
                        // Set readplan
                        r.ParamSet("/reader/read/plan", plan);
#else
                        List<ReadPlan> planList = new List<ReadPlan>();
                        foreach (TagProtocol protocol in protocolList)
                        {
                            planList.Add(new StopTriggerReadPlan(sotc,antennaList, protocol, null, null, 1000));
                        }
                        MultiReadPlan plan = new MultiReadPlan(planList);
                        // Set read plan
                        r.ParamSet("/reader/read/plan", plan);
#endif
                    }
                    else
                    {
                        List<ReadPlan> planList = new List<ReadPlan>();
                        foreach (TagProtocol protocol in protocolList)
                        {
                            planList.Add(new StopTriggerReadPlan(sotc, antennaList, protocol, null, null, 1000));
                        }
                        MultiReadPlan plan = new MultiReadPlan(planList);
                        // Set readplan
                        r.ParamSet("/reader/read/plan", plan);
                    }
#endif
#if ENABLE_SYNC_READ
                    TagReadData[] tagReads;
                    // Read tags
                    tagReads = r.Read(1000);
                    // Print tag reads
                    foreach (TagReadData tr in tagReads)
                    {
                        Console.WriteLine(tr.ToString() + " protocol:" + tr.Tag.Protocol.ToString());
                        totalTagCount += tr.ReadCount;
                    }
#endif
#if ENABLE_ASYNC_READ
                    // Create and add tag listener
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                    {
                        totalTagCount += e.TagReadData.ReadCount;
                        Console.WriteLine("Background read: " + e.TagReadData + " protocol:" + e.TagReadData.Tag.Protocol);
                    };
                    // Create and add read exception listener
                    r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);
                    // Search for tags in the background
                    r.StartReading();
                    while (!r.isReadStopped())
                    { }
#endif
                    Console.WriteLine("\nTotal tag count: " + totalTagCount);
                }
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

        #region Exception Listener
#if ENABLE_ASYNC_READ
        private static void r_ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Console.WriteLine("Error: " + e.ReaderException.Message);
        }
#endif
        #endregion

    }
}