using System;
using System.Collections.Generic;
using System.Text;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;
namespace MultireadAsync
{
    /// <summary>
    /// Sample program that reads tags on multiple readers and prints the tags found.
    /// </summary>
    class MultireadAsync
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader1-uri] [--ant n[,n...]] [reader2-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example: 'tmr:///com4 --ant 1,2 tmr:///com12 --ant 1,2' or '-v tmr:///com4 --ant 1,2 tmr:///com12 --ant 1,2'"
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
            string readerName = null;
            int[] antennaList = null;
            Dictionary<string, int[]> readerPort = new Dictionary<string, int[]>();
            for (int nextarg = 0; nextarg < args.Length; nextarg++)
            {
                string arg = args[nextarg];
                if (arg.Equals("--ant"))
                {
                    if (null != readerName)
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
                        Usage();
                    }
                }
                else
                {
                    if (null != readerName)
                    {
                        readerPort.Add(readerName,antennaList);
                        antennaList = null;
                    }
                    readerName = arg;
                }
            }

            if (null != readerName)
            {
                // Output the previously-parsed reader name and antenna list
                readerPort.Add(readerName, antennaList);
                readerName = null;
                antennaList = null;
            }

            try
            {
                Reader[] r = new Reader[readerPort.Count];
                int i = 0;
                
                foreach (KeyValuePair<string, int[]> pair in readerPort)
                {
                    r[i] = Reader.Create(pair.Key);
                    //Uncomment this line to add default transport listener.
                    //r[i].Transport += r[i].SimpleTransportListener;

                    Console.WriteLine("Created Reader {0},{1}",i,(string)r[i].ParamGet("/reader/uri"));
                    r[i].Connect();
                    
                    if (Reader.Region.UNSPEC == (Reader.Region)r[i].ParamGet("/reader/region/id"))
                    {
                        Reader.Region[] supportedRegions = (Reader.Region[])r[i].ParamGet("/reader/region/supportedRegions");
                        if (supportedRegions.Length < 1)
                        {
                            throw new FAULT_INVALID_REGION_Exception();
                        }
                        r[i].ParamSet("/reader/region/id", supportedRegions[0]);
                    }
                    if (r[i].isAntDetectEnabled(antennaList) && readerPort[pair.Key] == null)
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }
                    r[i].NoofMultiReaders = true;
                    // Create a simplereadplan which uses the antenna list created above
                    SimpleReadPlan plan = new SimpleReadPlan(pair.Value, TagProtocol.GEN2,null,null,1000);
                    // Set the created readplan
                    r[i].ParamSet("/reader/read/plan", plan);

                    // Create and add tag listener
                    r[i].TagRead += PrintTagreads;
                    // Create and add read exception listener
                    r[i].ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);
                    // Search for tags in the background
                    r[i].StartReading();
                    i++;
                    
                }
                
                Console.WriteLine("\r\n<Do other work here>\r\n");
                Thread.Sleep(5000);
                Console.WriteLine("\r\n<Do other work here>\r\n");
                Thread.Sleep(5000);

                for (int j = 0; j < readerPort.Count; j++)
                {
                    r[j].StopReading();
                    r[j].Destroy();
                }
                //r[i].NoofMultiReaders = false;
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
       
        static void r_ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Reader r = (Reader)sender;
            Console.WriteLine("Exception reader uri {0}",(string)r.ParamGet("/reader/uri"));
            Console.WriteLine("Error: " + e.ReaderException.Message);
        }
        static void PrintTagreads(Object sender, TagReadDataEventArgs e)
        {
            Reader r = (Reader)sender;
            Console.WriteLine("Background read:" + (string)r.ParamGet("/reader/uri") + e.TagReadData);
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
