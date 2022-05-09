using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Management;

// Reference the API
using ThingMagic;

namespace DeviceDetection
{
    class DeviceDetection
    {
        static void Main(string[] args)
        {
            Dictionary<string, string> readers = new Dictionary<string, string>();
            // Gets the Serial reader list
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0"))
            {
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    if ((queryObj != null) && (queryObj["Name"] != null))
                    {
                        try
                        {
                            if (queryObj["Name"].ToString().Contains("(COM"))
                            {
                                string portNumber = Regex.Match((queryObj["Name"].ToString()), @"(?<=\().+?(?=\))").Value.ToString();
                                if (!(readers.ContainsKey(portNumber)))
                                {
                                    readers.Add(portNumber, "tmr:///com" + portNumber.Replace("COM", ""));
                                }
                            }
                        }
                        catch (Exception)
                        {
                            //If reader is throwing error for connect, we are not going show in the reader name list..
                            Console.WriteLine("Detected device" + queryObj["Name"].ToString() + " has Generic Name and Failed to connect.");
                        }
                    }
                }
            }

            // Prints detected and requested readers details
            foreach (KeyValuePair<string, string> pair in readers)
            {
                DeviceDetection reader = new DeviceDetection();
                reader.ReaderDescription(pair.Value);
                Console.WriteLine();
            }
        }
        /// <summary>
        /// Connect to the reader and Retrive reader details
        /// </summary>
        /// <param name="reader">Reader URI</param>
        private void ReaderDescription(string reader)
        {
            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automatic
                // reader shutdown (using IDisposable interface).
                using (Reader r = Reader.Create(reader))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

                    r.ParamSet("/reader/transportTimeout", 100);
                    r.ParamSet("/reader/commandTimeout", 100);

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

                    // Create reader information obj
                    DeviceDetection readInfo = new DeviceDetection();
                    Console.WriteLine("Reader information of connected reader");

                    // Reader uri info
                    readInfo.Get("Reader URI", "/reader/uri", r);

                    // Hardware info
                    readInfo.Get("Hardware", "/reader/version/hardware", r);

                    // Serial info
                    readInfo.Get("Serial", "/reader/version/serial", r);

                    // Model info
                    readInfo.Get("Model", "/reader/version/model", r);

                    // Software info
                    readInfo.Get("Software", "/reader/version/software", r);

                    // Product id info
                    readInfo.Get("Product ID", "/reader/version/productID", r);

                    // Product group id info
                    readInfo.Get("Product Group ID", "/reader/version/productGroupID", r);

                    // Product group info
                    readInfo.Get("Product Group", "/reader/version/productGroup", r);

                }
            }
            catch (Exception)
            {
                //Exception raised because the device detected is unsupported
            }
        }

        /// <summary>
        /// Get the data for the specified parameter from the connected reader
        /// </summary>
        /// <param name="paramString">Parameter descritpion</param>
        /// <param name="parameter">Parameter to get</param>
        /// <param name="rdrObj">Reader object</param>        
        public void Get(string paramString, string parameter, Reader rdrObj)
        {
            try
            {
                // Get data for the requested parameter from the connected reader
                Console.WriteLine();
                Console.WriteLine(paramString + ": " + rdrObj.ParamGet(parameter));
            }
            catch (Exception ex)
            {
                if ((ex is FeatureNotSupportedException) || (ex is ArgumentException))
                {
                    Console.WriteLine(paramString + ": " + parameter + " - Unsupported");
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
