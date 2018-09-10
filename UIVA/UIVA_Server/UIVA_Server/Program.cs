﻿///-----------------------------------------------------------------------------------
///-----------------------------------------------------------------------------------
/// UIVA Server - Unity Indie VRPN Adapter 
/// 
/// Function:
/// 
///     UIVA is a middle-ware between VRPN and Unity. It enables games developed by Unity3D INDIE
///   to be controlled by devices powered by VRPN. It has a client and a server simultaneously.
///   For VRPN, UIVA is its client which implements several callback functions to receive the 
///   latest data from the devices. For Unity, UIVA is a server that stores the latest sensor
///   data which allows it to query. The framework is shown as below:
///   
///        ~~~Sensor~~~      ~~~VRPN~~~      ~~~~~~~~~~~~UIVA~~~~~~~~~~~~~~~    ~~~Unity3D~~~     
///        
///   Kinect-----(FAAST)---->|--------|    |--------|--------|    |---------|
///    Wii ----(VRPN Wii)--->|        |    |        |        |    |         |--->Object transform
///   BPack --(VRPN BPack)-->|  VRPN  |    |  VRPN  | Unity  |    |  Unity  |
///           ...            |        |===>|  .net  | socket |===>|  socket |--->GUI
///           ...            | server |    |        |        |    |         |
///           ...            |        |    | client | server |    |  client |--->etc. of Unity3D
///           ...            |--------|    |--------|--------|    |---------|
///    
/// Special note: 
///
///      The VRPNWrapper implemented by the AR lab of Georgia Institute of Technology offers
///   a easier to use wrapper of VRPN to be used as a plugin in Unity3D Pro. If you can afford 
///   the Pro version of Unity. I suggest you to use VRPNWrapper. Their website is:
///           https://research.cc.gatech.edu/uart/content/about
///   They also implemented a ARToolkit wrapper which enables AR application in Unity. 
///   Check out their UART project, it is awesome!
///    
/// Author: 
/// 
/// Jia Wang (wangjia@wpi.edu)
/// Human Interaction in Virtual Enviroments (HIVE) Lab
/// Department of Computer Science
/// Worcester Polytechnic Institute
/// 
/// History: (1.0) 02/05/2011  by  Jia Wang
///
/// Acknowledge: Thanks to Chris VanderKnyff for the .NET version of VRPN
///                     to UNC for the awesome VRPN
///                     to Unity3D team for the wonderful engine
///              
///              and above all, special thanks to 
///                 Prof. Robert W. Lindeman (http://www.wpi.edu/~gogo) 
///              for the best academic advice.
///              
///-----------------------------------------------------------------------------------
///-----------------------------------------------------------------------------------

//#define __VERBOSE__

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;            // For configuration file
using System.Net.Sockets;   // For communicating with Unity3D
using System.Net;
using Vrpn;                 // For communicating with VRPN server
using System.Threading;
namespace UIVA_Server      // Unity Indie VRPN Adapter
{
    class UIVA
    {
        private bool stopAll = false;

        // How many certain devices are alive.
        // If 2 mice are alive, UIVA_Client can request data from mouse 1 and 2
        // If miceAlive < the # of mouse request, then return a error
        private int biosemiAlive = 0;

        // UIVA_Device objects
        // For Forward Communication        
        private List<VRPN_ButtonServer> biosemi = new List<VRPN_ButtonServer>();

        // For Reverse Communication
        private List<UIVA_ButtonServer> buttonServer = new List<UIVA_ButtonServer>();


        // Socket stuff used for handling Unity communication
        private Socket socListener;                 // Socket listen to client request
        private Socket socWorker;                   // Socket handle connection with client
        private byte[] recBuffer = new byte[100];   // Receive buffer
        private string recStr = "";                 // Deciphered receive buffer
        private byte[] sendBuffer = new byte[100];  // Ciphered send buffer
        private string sendStr = "";                // String going to be send
        private byte[] orderBuffer = new byte[100];
        private string sendOrder = "";
        private string ButtonNum = "";
        private int i = 0;
        private int j = 0;
        private ASCIIEncoding encoder;              // Encoding, decoding

        public UIVA()
        {
            // 1. Parse config file and wake up selected devices
            ParseConfigFile();

            // 2. Initiate Unity server and start its thread
            //Create a socket for listening connections
            socListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            //Create a socket for message transmissions
            socWorker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            //Create a encoder to translate string into byte stream
            encoder = new ASCIIEncoding();
            //Port: 8881
            IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, 8881);
            socListener.Bind(ipLocal);

            // 3. Start listening to Unity3D
            TalkToUnity();
        }

        /// <summary>
        /// Parse config file and activate the devices selected
        /// </summary>
        /// <returns></returns>
        private void ParseConfigFile()
        {
            Console.WriteLine("--------------------------------------------------------------------------");
            Console.WriteLine("                  UIVA 1.0: Unity Indie VRPN Adapter  (Server)            ");
            Console.WriteLine("                     - Modified for P300 interface -                      ");
            Console.WriteLine("                      BCI Lab in Handong Global Univ.                     ");
            Console.WriteLine("--------------------------------------------------------------------------\n");

            Console.WriteLine("Parsing configuration file...");
            StreamReader cfgFile;
            String line;
            cfgFile = File.OpenText(".\\UIVA_Server.cfg");
            line = cfgFile.ReadLine();
            while (line != null)
            {
                if (line.Length != 0 && line[0] != '#')
                {
                    String[] tokens;
                    tokens = line.Split(new char[] { ' ' });
                    String name = tokens[1];
                    switch (tokens[0])
                    {
                        case "DEV_BIOSEMI":
                            buttonServer.Add(new UIVA_ButtonServer());
                            Console.WriteLine("\n^_^ < VRPN > --- UIVA_ButtonServer started...");

                            biosemi.Add(new VRPN_ButtonServer(name));
                            biosemiAlive++;
                            Console.WriteLine("\n^_^ < VRPN > --- BIOSEMI # {0} tracking started...", biosemiAlive);
                            break;
                        default:
                            throw new Exception("Config file error: invalid device name");
                    }
                }
                line = cfgFile.ReadLine();
            }
            cfgFile.Close();
            buttonServer[0].WakeUpTest();
        }

        /// <summary>
        /// Unity3D server thread, talk to Unity3D to respond requests
        /// </summary>
        /// <returns></returns>
        private void TalkToUnity()
        {
            bool connected = false;
            while (!stopAll)
            {
                if (!connected)
                {
                    //Can hold 4 client requests in the queue
                    socListener.Listen(4);
                    socWorker = socListener.Accept();
                    connected = true;
                    Console.WriteLine("\n^_^ < Unity3D > --- Connection Erected");
                }
                else
                {
                    try
                    {
                        //디버깅 용도
                        /*for ( ; ; )
                        {
                            biosemi[0].Update();
                            Thread.Sleep(1000);
                            Console.WriteLine(biosemi[0].b_up + " " + biosemi[0].b_down + " " + biosemi[0].b_left + " " + biosemi[0].b_right + " " + ArrowDirection);
                            ArrowDirection = biosemi[0].Encode();
                        }*/
                        socWorker.Receive(recBuffer);               //Get request message
                        recStr = encoder.GetString(recBuffer);      //Convert to string
                        int index = recStr.IndexOf('\n');           //Parse string: remove '\n'
                        recStr = recStr.Remove(index);
                        int butts;                                  // Get button number from client 

                        String[] tokens = recStr.Split(new char[] { '?' });
                        int which = 0;
                        if (tokens[0] != "Ready" && tokens[0] != "Bye" && tokens[0] != "Press" && tokens[0] != "Release" && tokens[0] != "Press_O")
                        {
                            which = System.Convert.ToInt32(tokens[1]);
                        }

                        switch (tokens[0])
                        {
                            case "Ready":          //Ready confirm
                                sendStr = "Ready!\n";
                                break;

                            case "Press": //target과 nontarget을 구분하기 위해 target은 1의 자리가 1이 되고 nontarget은 0이 된다.
                                butts = System.Convert.ToInt32(tokens[1]);
                                i = butts / 10;
                                j = butts % 10;
                                switch (i)
                                {
                                    case 0:
                                        ButtonNum = "Zero";
                                        break;
                                    case 1:
                                        ButtonNum = "One";
                                        break;
                                    case 2:
                                        ButtonNum = "Two";
                                        break;
                                    case 3:
                                        ButtonNum = "Three";
                                        break;
                                    case 4:
                                        ButtonNum = "Four";
                                        break;
                                    case 5:
                                        ButtonNum = "Five";
                                        break;
                                    case 6:
                                        ButtonNum = "Six";
                                        break;
                                    case 7:
                                        ButtonNum = "Seven";
                                        break;
                                    default:
                                        break;
                                }
                                Console.WriteLine("\nBlink " + ButtonNum + "\nSending button " + j);
                                buttonServer[0].Press(j);
                                sendStr = "";
                                break;

                            case "Press_O":
                                butts = System.Convert.ToInt32(tokens[1]);
                                switch (butts)
                                {
                                    case 0:
                                        ButtonNum = "Zero";
                                        break;
                                    case 1:
                                        ButtonNum = "One";
                                        break;
                                    case 2:
                                        ButtonNum = "Two";
                                        break;
                                    case 3:
                                        ButtonNum = "Three";
                                        break;
                                    case 4:
                                        ButtonNum = "Four";
                                        break;
                                    case 5:
                                        ButtonNum = "Five";
                                        break;
                                    case 6:
                                        ButtonNum = "Six";
                                        break;
                                    case 7:
                                        ButtonNum = "Seven";
                                        break;
                                    default:
                                        break;
                                }
                                Console.WriteLine("\nBlink " + ButtonNum + "\nSending button " + butts);
                                buttonServer[0].Press(butts);
                                sendStr = "";
                                break;

                            case "Biosemi":
                                if (which <= biosemiAlive)
                                {
                                    // Encode the data in a message
                                    //Console.WriteLine("reading...");
                                    biosemi[0].Update();
                                    //Console.WriteLine("first try" + biosemi[0].b_up + " " + biosemi[0].b_down + " " + biosemi[0].b_left + " " + biosemi[0].b_right);
                                    sendOrder = biosemi[0].Encode();
                                    while (sendOrder == "No_Value\n")
                                    {
                                        Thread.Sleep(500);
                                        biosemi[0].Update();
                                        //Console.WriteLine(biosemi[0].b_up + " " + biosemi[0].b_down + " " + biosemi[0].b_left + " " + biosemi[0].b_right);
                                        sendOrder = biosemi[0].Encode();
                                    }
                                    Console.WriteLine("sendOrder: " + sendOrder);
                                    orderBuffer = encoder.GetBytes(sendOrder); //Encode
                                    socWorker.Send(orderBuffer);             //Send
                                }
                                else
                                {
                                    sendStr = "ERROR: biosemi # 1 is not turned on\n";
                                }
                                break;

                            case "Bye":            //Disconnect confirm
                                sendStr = "Bye!\n";
                                connected = false;
                                Console.WriteLine("^_^ < Unity3D > --- Connection Ended");
                                break;

                            default:
                                sendStr = "ERROR: invalid request, what do you mean <" + recStr + ">\n";
                                break;
                        }

                        if (tokens[0] != "Press" && tokens[0] != "Release" && tokens[0] != "Biosemi")
                        {
                            sendBuffer = encoder.GetBytes(sendStr); //Encode
                            socWorker.Send(sendBuffer);             //Send
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("*.* < Unity3D > --- Connection Lost");
#if __VERBOSE__
                        Console.WriteLine(e.ToString());
#endif
                        connected = false;
                    }
                }
            }   // End of while(!stopAll) loop
        } // End of thread TalkToUnity()

        public static void Main(string[] argv)
        {
            UIVA theUIVA = new UIVA();
        }
    } // End of class UIVA
}// End of namespace UIVA