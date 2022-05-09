using System;
using System.Collections.Generic;
using System.Text;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;

namespace ReadAsync
{
    /// <summary>
    /// Sample program that reads tags in the background and prints the
    /// tags found.
    /// </summary>
    class Program
    {
        /// <summary>
        /// This indicates the read time of async read, i.e., sleep time between start and stop read.
        /// </summary>
        private const double SLEEP_TIME = 5000;

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

                    string model = (string)r.ParamGet("/reader/version/model").ToString();
                    if (!model.Equals("M3e"))
                    {
                        if (r.isAntDetectEnabled(antennaList))
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

                    // Create a simplereadplan which uses the antenna list created above
                    SimpleReadPlan plan;
                    if (model.Equals("M3e"))
                    {
                        // initializing the simple read plan
                        plan = new SimpleReadPlan(antennaList, TagProtocol.ISO14443A, null, null, 1000);
                    }
                    else
                    {
                        plan = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, null, 1000);
                    }

                    // Set the created readplan
                    r.ParamSet("/reader/read/plan", plan);

                    // Create and add tag listener
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                    {
                        Console.WriteLine("Background read: " + e.TagReadData);
                    };

                    // Create and add read exception listener
                    r.ReadException += delegate(object sender, ReaderExceptionEventArgs e)
                    {
                        if (r.lastReportedException == null || (r.lastReportedException == null) ? true : (!e.ReaderException.Message.Contains(r.lastReportedException.Message)))
                        {
                            Console.WriteLine("Error: " + e.ReaderException.Message);
                        }

                        r.lastReportedException = e.ReaderException;
                    };

                    // Search for tags in the background
                    r.StartReading();

                    // Exit the while loop,
                    //1. When error occurs 
                    //2. When sleep timeout expires

                    ///Capture the start time before starting the read.
                    DateTime startTime = DateTime.Now;
                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    while (true)
                    {
                        if (ValidateReadTime(startTime))
                        {
                            //break if sleep timeout expired    
                            break;
                        }

                        //Exit the process if any error occured
                        if (r.lastReportedException != null)
                        {
                            ErrorHandler(r);
                            //Can add recovery mechanism here
                            //Do some work here 
                            Environment.Exit(1);
                        }

                        Thread.Sleep(1);
                    }

                    r.StopReading();

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

        /// <summary>
        /// Camparre the time for Async read
        /// </summary>
        /// <param name="time">Start Time</param>
        /// <returns>bool</returns>
        private static bool ValidateReadTime(DateTime time)
        {
            TimeSpan tr = DateTime.Now - time;
            if (tr.TotalMilliseconds >= SLEEP_TIME)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Function to handle different errors received
        /// </summary>
        /// <param name="r"></param>
        private static void ErrorHandler(Reader r)
        {
            ReaderException re = r.lastReportedException;
            switch (re.Message)
            {
                case "The reader received a valid command with an unsupported or invalid parameter":
                case "Unimplemented feature.":
                    r.StopReading();
                    r.Destroy();
                    break;
                default:
                    r.Destroy();
                    break;
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
