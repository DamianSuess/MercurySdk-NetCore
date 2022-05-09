using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;

// Reference the API
using ThingMagic;

namespace GpioCommands
{
    class GpioCommands
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [Gpio options]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [Gpio options] :",
                    " get-gpi -- Read input pins",
                    " set-gpo -- Write output pin(s)",
                    " set-gpo [[2,1],[3,1]]",
                    " testgpiodirection -- verifying gpio directionality"
                }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            // Program setup
            if (2 > args.Length)
            {
                Usage();
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
                    for (int nextarg = 1; nextarg < args.Length; nextarg++)
                    {
                        string arg = args[nextarg];
                        if (arg.Equals("get-gpi"))
                        {
                            GpioPin[] gpilist = r.GpiGet();
                            Array arr = (Array)gpilist;
                            string[] valstrings = new string[arr.Length];
                            for (int i = 0; i < arr.Length; i++)
                            {
                                valstrings[i] = arr.GetValue(i).ToString();
                            }

                            StringBuilder sb = new StringBuilder();
                            sb.Append("[");
                            sb.Append(String.Join(",", valstrings));
                            sb.Append("]");
                            Console.WriteLine(sb.ToString());
                        }
                        else if (arg.Equals("set-gpo"))
                        {
                            try
                            {
                                ArrayList list = new ArrayList();
                                string str = args[nextarg + 1];
                                if (str.StartsWith("[") && str.EndsWith("]"))
                                {
                                    // Array of arrays
                                    if ('[' == str[1])
                                    {
                                        int open = 1;
                                        while (-1 != open)
                                        {
                                            int close = str.IndexOf(']', open);
                                            string Substr = str.Substring(open, (close - open + 1));
                                            ArrayList Sublist = new ArrayList();
                                            foreach (string eltstr in Substr.Substring(1, Substr.Length - 2).Split(new char[] { ',' }))
                                            {
                                                if (0 < eltstr.Length) { Sublist.Add(int.Parse(eltstr)); }
                                            }
                                            list.Add(Sublist);
                                            open = str.IndexOf('[', close + 1);
                                        }
                                    }
                                }
                                GpioPin[] gps = ArrayListToGpioPinArray(list);
                                r.GpoSet(gps);
                                Console.WriteLine("set-gpo success with: " + str);
                            }
                            catch (IndexOutOfRangeException)
                            {
                                Console.WriteLine("Missing argument after args[{0:d}] \"{1}\"", nextarg, args[nextarg]);
                                Usage();
                            }
                            nextarg++;
                        }
                        else if (arg.Equals("testgpiodirection"))
                        {
                            int[] input = new int[] { 1 };
                            int[] output = new int[] { 2 };

                            r.ParamSet("/reader/gpio/inputList", input);
                            Console.WriteLine("Input list set.");
                            r.ParamSet("/reader/gpio/outputList", output);
                            Console.WriteLine("Output list set.");

                            int[] inputList = (int[])(r.ParamGet("/reader/gpio/inputList"));
                            foreach (int i in inputList)
                            {
                                Console.WriteLine("input list={0}", i);
                            }

                            int[] outputList = (int[])(r.ParamGet("/reader/gpio/outputList"));
                            foreach (int i in outputList)
                            {
                                Console.WriteLine("output list={0}", i);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Argument {0}:\"{1}\" is not recognized", nextarg, arg);
                            Usage();
                        }
                    }
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
        private static GpioPin[] ArrayListToGpioPinArray(ArrayList list)
        {
            List<GpioPin> gps = new List<GpioPin>();
            foreach (ArrayList innerlist in list)
            {
                int[] pinval = (int[])innerlist.ToArray(typeof(int));
                int id = pinval[0];
                bool high = (0 != pinval[1]);
                GpioPin gp = new GpioPin(id, high);
                gps.Add(gp);
            }
            return gps.ToArray();
        }
    }
}