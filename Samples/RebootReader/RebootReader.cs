using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace RebootReader
{
    /// <summary>
    /// Sample program that reboots the reader
    /// </summary>
    class Program
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " Example: 'tmr:///com4'' or '-v tmr:///com4'"
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

            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automatic
                // reader shutdown (using IDisposable interface).
                Reader r;
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
                    r.Reboot();
                    r.Destroy();
                    int retryCount = 1;
                    bool notConnected = true;

                    /*For universal reader URI scheme i.e tmr: the create has to be inside the loop. 
                     * In order to use the create method out side the loop use product specific URI
                     * scheme i.e llrp: or rql:                     
                     */
                    do
                    {
                        try
                        {
                            Console.WriteLine("Trying to reconnect.... Attempt: " + retryCount);
                            r = Reader.Create(args[0]);
                            //r.Transport += r.SimpleTransportListener;
                            r.Connect();
                            notConnected = false;
                        }
                        catch (Exception)
                        {

                        }
                        finally
                        {
                            r.Destroy();
                        }
                        retryCount++;
                    } while (notConnected);
                    Console.WriteLine("Reader is reconnected successfully");
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
    }
}