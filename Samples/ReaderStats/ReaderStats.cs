using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;
using System.Threading;

namespace ReaderStats
{
    /// <summary>
    /// Sample program that supports reader stats functionality
    /// </summary>
    class ReaderStats
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

                    string model = r.ParamGet("/reader/version/model").ToString();
                    if (!model.Equals("M3e"))
                    {
                        if (r.isAntDetectEnabled(antennaList))
                        {
                            Console.WriteLine("module doesn't has antenna detection support please provide antenna list");
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

                    if ((model.Equals("M6e Micro") || model.Equals("M6e Micro USBPro") || model.Equals("M6e Micro USB")))
                    {
                        r.ParamSet("/reader/antenna/checkPort", true);
                    }

                    SimpleReadPlan plan;

                    if (model.Equals("M3e"))
                    {
                        // Create a simplereadplan which uses the antenna list created
                        plan = new SimpleReadPlan(antennaList, TagProtocol.ISO14443A, null, null, 1000);
                    }
                    else
                    {
                        plan = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, null, 1000);
                    }

                    // Set the created readplan
                    r.ParamSet("/reader/read/plan", plan);

                    // Request all reader stats
                    r.ParamSet("/reader/stats/enable", Reader.Stat.StatsFlag.ALL);

                    Reader.Stat.StatsFlag get = (Reader.Stat.StatsFlag)r.ParamGet("/reader/stats/enable");

                    Console.WriteLine("Get requested reader stats : " + r.ParamGet("/reader/stats/enable").ToString());

                    TagReadData[] tagReads;
                    Reader.Stat.Values objRdrStats = null;

                    #region Perform sync read

                    for (int iteration = 1; iteration <= 1; iteration++)
                    {
                        Console.WriteLine("Iteration: " + iteration);
                        Console.WriteLine("Performing the search operation for 1 sec");
                        tagReads = null;
                        objRdrStats = null;
                        // Read tags
                        tagReads = r.Read(1000);
                        foreach (TagReadData tag in tagReads)
                        {
                            Console.WriteLine((model.Equals("M3e") ? "UID: " : "EPC: ") + tag.EpcString);
                        }
                        Console.WriteLine("Search is completed. Get the reader stats");
                        objRdrStats = (Reader.Stat.Values)r.ParamGet("/reader/stats");
                        Console.WriteLine(objRdrStats.ToString());
                        Console.WriteLine();
                        if (!model.Equals("M3e"))
                        {
                            Int32[][] objAntennaReturnLoss = (Int32[][])r.ParamGet("/reader/antenna/returnLoss");
                            Console.WriteLine("Antenna Return Loss");
                            foreach (Int32[] antennaLoss in objAntennaReturnLoss)
                            {
                                Console.WriteLine(" Antenna {0:D} | {1:D}", antennaLoss[0], antennaLoss[1]);
                            }
                            Console.WriteLine();
                        }
                    }

                    #endregion Perform sync read

                    #region Perform async read

                    Console.WriteLine();
                    Console.WriteLine("Performing async read for 1 sec");

                    #region Create and add listeners

                    // Create and add tag listener
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                    {
                        Console.WriteLine("Background read: " + e.TagReadData);
                    };

                    // Create and add read exception listener
                    r.ReadException += r_ReadException;

                    // Add reader stats listener
                    r.StatsListener += r_StatsListener;

                    #endregion Create and add listeners

                    // Search for tags in the background
                    r.StartReading();

                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);
                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);

                    r.StopReading();

                    #endregion Perform async read
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

        static void r_StatsListener(object sender, StatsReportEventArgs e)
        {
            Console.WriteLine(e.StatsReport.ToString());
            Console.WriteLine();
        }

        static void r_ReadException(object sender, ReaderExceptionEventArgs e)
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