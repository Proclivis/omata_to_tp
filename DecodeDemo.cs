#region Copyright
////////////////////////////////////////////////////////////////////////////////
// Copyright 2018 Michael A Jones

// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is furnished 
// to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS 
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR 
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER 
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN 
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////
#endregion

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using Dynastream.Fit;
using CommandLine;

class Options
{
    [Option]
    public string InFile { get; set; }

    [Option]
    public string OutFile { get; set; }

    [Option(Default=0.014F)]
    public float WheelInertia { get; set; }

    [Option(Default = 0.321F)]
    public float WheelRadius { get; set; }

    [Option(Default = 0.0032F)]
    public float WheelResistanceCoefficient { get; set; }

    [Option(Default = 1.0F)]
    public float AirDragCofficient { get; set; }

    [Option(Default = 0.25F)]
    public float AirDragArea { get; set; }

    [Option(Default = 0.0F)]
    public float DraftingPercentage { get; set; }

}

namespace DecodeDemo
{
    class Program
    {
        static Dictionary<ushort, int> mesgCounts = new Dictionary<ushort, int>();
        static FileStream fitSource;
        static FileStream fitDest;
        //static List<RecordMesg> records = new List<RecordMesg>();
        static Encode encodeDemo = new Encode(ProtocolVersion.V20);
        static float lastAltitude = 0F;
        static float lastDistance = 0F;
        static float lastSpeed = 0F;
        static uint lastTime = 0;
        static float lastPower = 0F;
        static bool haveAltitude = false;
        static Options options;

        static void Main(string[] args)
        {
            CommandLine.Parser.Default.ParseArguments<Options>(args)
                       .WithParsed<Options>(opts => RunOptionsAndReturnExitCode(opts));
//            .WithNotParsed<Options>((errs) => HandleParseError(errs));
        }

        static void RunOptionsAndReturnExitCode(Options opts)
        {
            options = opts;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Console.WriteLine("FIT Decode Example Application");

            try
            {
                fitDest = new FileStream(options.OutFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                Console.WriteLine("Opening Destintion {0}", options.OutFile);
                // Write our header
                encodeDemo.Open(fitDest);

                // Attempt to open .FIT file
                fitSource = new FileStream(options.InFile, FileMode.Open);
                Console.WriteLine("Opening Source {0}", options.InFile);

                Decode decodeDemo = new Decode();
                MesgBroadcaster mesgBroadcaster = new MesgBroadcaster();

                // Connect the Broadcaster to our event (message) source (in this case the Decoder)
                decodeDemo.MesgEvent += mesgBroadcaster.OnMesg;
                decodeDemo.MesgDefinitionEvent += mesgBroadcaster.OnMesgDefinition;
                //decodeDemo.DeveloperFieldDescriptionEvent += OnDeveloperFieldDescriptionEvent;

                // Subscribe to message events of interest by connecting to the Broadcaster
                mesgBroadcaster.MesgEvent += OnMesg;
                mesgBroadcaster.MesgDefinitionEvent += OnMesgDefn;

                mesgBroadcaster.FileIdMesgEvent += OnFileIDMesg;
                mesgBroadcaster.UserProfileMesgEvent += OnUserProfileMesg;
                mesgBroadcaster.MonitoringMesgEvent += OnMonitoringMessage;
                mesgBroadcaster.DeviceInfoMesgEvent += OnDeviceInfoMessage;
                mesgBroadcaster.RecordMesgEvent += OnRecordMessage;

                bool status = decodeDemo.IsFIT(fitSource);
                status &= decodeDemo.CheckIntegrity(fitSource);

                // Process the file
                if (status)
                {
                    Console.WriteLine("Translating...");
                    decodeDemo.Read(fitSource);
                    Console.WriteLine("Translated FIT file {0}", options.InFile);
                }
                else
                {
                    try
                    {
                        Console.WriteLine("Integrity Check Failed {0}", options.InFile);
                        if (decodeDemo.InvalidDataSize)
                        {
                            Console.WriteLine("Invalid Size Detected, Attempting to decode...");
                            decodeDemo.Read(fitSource);
                        }
                        else
                        {
                            Console.WriteLine("Attempting to decode by skipping the header...");
                            decodeDemo.Read(fitSource, DecodeMode.InvalidHeader);
                        }
                    }
                    catch (FitException ex)
                    {
                        Console.WriteLine("Translate caught FitException: " + ex.Message);
                    }
                }
                fitSource.Close();
 
                // Update header datasize and file CRC
                encodeDemo.Close();
                fitDest.Close();

                Console.WriteLine("");
                Console.WriteLine("Summary:");
                int totalMesgs = 0;
                foreach (KeyValuePair<ushort, int> pair in mesgCounts)
                {
                    Console.WriteLine("MesgID {0,3} Count {1}", pair.Key, pair.Value);
                    totalMesgs += pair.Value;
                }

                Console.WriteLine("{0} Message Types {1} Total Messages", mesgCounts.Count, totalMesgs);

                stopwatch.Stop();
                Console.WriteLine("");
                Console.WriteLine("Time elapsed: {0:0.#}s", stopwatch.Elapsed.TotalSeconds);
                Console.ReadKey();
            }
            catch (FitException ex)
            {
                Console.WriteLine("A FitException occurred when trying to decode the FIT file. Message: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception occurred when trying to decode the FIT file. Message: " + ex.Message);
            }
        }

        private static void OnDeveloperFieldDescriptionEvent(object sender, DeveloperFieldDescriptionEventArgs args)
        {
            Console.WriteLine("New Developer Field Description");
            Console.WriteLine("   App Id: {0}", args.Description.ApplicationId);
            Console.WriteLine("   App Version: {0}", args.Description.ApplicationVersion);
            Console.WriteLine("   Field Number: {0}", args.Description.FieldDefinitionNumber);

        }

        #region Message Handlers
        // Client implements their handlers of interest and subscribes to MesgBroadcaster events
        static void OnMesgDefn(object sender, MesgDefinitionEventArgs e)
        {

            if (e.mesgDef.LocalMesgNum == 0)
            {
                Field field = new Field(RecordMesg.FieldDefNum.Power, 0x84);
                FieldDefinition definition = new FieldDefinition(field); // Tagg
                e.mesgDef.AddField(definition);
                e.mesgDef.NumFields++;
            }
            if (e.mesgDef.LocalMesgNum == 1)
            {
                Field field = new Field(RecordMesg.FieldDefNum.Power, 0x84);
                FieldDefinition definition = new FieldDefinition(field); // Tagg
                e.mesgDef.AddField(definition);
                e.mesgDef.NumFields++;
            }

            encodeDemo.Write(e.mesgDef);

        }

        static void OnMesg(object sender, MesgEventArgs e)
        {
            if (mesgCounts.ContainsKey(e.mesg.Num))
            {
                mesgCounts[e.mesg.Num]++;
            }
            else
            {
                mesgCounts.Add(e.mesg.Num, 1);
            }

            if (e.mesg.LocalNum != 0 && e.mesg.LocalNum != 1)
                encodeDemo.Write(e.mesg);
        }

        static void OnFileIDMesg(object sender, MesgEventArgs e)
        {
            encodeDemo.Write(e.mesg);
        }

        static void OnUserProfileMesg(object sender, MesgEventArgs e)
        {
            encodeDemo.Write(e.mesg);
        }

        static void OnDeviceInfoMessage(object sender, MesgEventArgs e)
        {
            encodeDemo.Write(e.mesg);
        }

        static void OnMonitoringMessage(object sender, MesgEventArgs e)
        {
            encodeDemo.Write(e.mesg);
        }

        private static object GetValue(Mesg mesg, byte fieldNumber)
        {
            object value = null;

            Field profileField = Profile.GetField(mesg.Num, fieldNumber);

            IEnumerable<FieldBase> fields = mesg.GetOverrideField(fieldNumber);

            foreach (FieldBase field in fields)
            {
                value = field.GetValue();
            }
            return value;
        }

        // Validation of a Mathemstical Model for Road Cycling Power
        //
        // Pad = 1/2 * rho * Cd * A * Va^2 * Vg
        // Power Air Drag, rho is air density, Cd is Drag Coefficient, A is area, Va is tangential air velocity, Vg is ground velocity
        //
        // Pwr = 1/2 * rho * Fw * Va^2 * Vg
        // Power Wheel Rotation
        //
        // Pat = 1/2 * rho * (Cd * A + Fw) * Vu^2 * Vg
        // Power Air Total
        //
        // Prr = Vg * COS[TAN^-1 Gr] * Crr * m * g
        // Prr ~= Vg * Crr * m * g (up to 10% grade)
        // Power rolling resistance, Crr is coefficient of rolling resistance
        //
        // Pwb = Vg * (92 + 8.7 * Vg) * 10^-3
        // Power Wheel Berings
        //
        // Ppe = Vg * m * g * SIN[TAN^-1 Gr]
        // Power Potential Energy, D is distance, Gr is road grade
        //
        // Pke = 1/2 * (m + I / r ^ 2) * (Vg2 ^ 2 - Vg1 ^ 2) / (t2 - t1)
        // Power Kentic Energy, I is moment of inertia

        private static void OnRecordMessage(object sender, MesgEventArgs e)
        {
            var recordMessage = (RecordMesg)e.mesg;

            if (recordMessage.LocalNum == 0 || recordMessage.LocalNum == 1)
            {
                // https://bicycles.stackexchange.com/questions/7774/how-do-i-calculate-the-power-required-to-climb-a-hill-at-a-given-cadence

                float currentAltitude = 0.0F;
                float currentDistance = 0.0F;
                float currentSpeed = 0.0F;
                uint currentTime = 0;
                float slope = 0.0F;
                //float mass = 83.5F; // Commute
                float mass = 80.0F; // Seven
                float power = 0.0F;
                float potentialPower = 0.0F;
                float keneticPower = 0.0F;
                float powerAirResistance = 0.0F;
                float powerRoadResistance = 0.0F;
                float rho = 1.2F; // Air density (no altitude or temperature calculation)

                if (!haveAltitude)
                {
                    lastAltitude = (float)GetValue(recordMessage, RecordMesg.FieldDefNum.Altitude);
                    lastDistance = (float)GetValue(recordMessage, RecordMesg.FieldDefNum.Distance);
                    lastSpeed = (float)GetValue(recordMessage, RecordMesg.FieldDefNum.Speed);
                    lastTime = (uint)GetValue(recordMessage, RecordMesg.FieldDefNum.Timestamp);
                    haveAltitude = true;
                }

                currentAltitude = (float)GetValue(recordMessage, RecordMesg.FieldDefNum.Altitude);
                currentDistance = (float)GetValue(recordMessage, RecordMesg.FieldDefNum.Distance);
                currentSpeed = (float)GetValue(recordMessage, RecordMesg.FieldDefNum.Speed);
                currentTime = (uint)GetValue(recordMessage, RecordMesg.FieldDefNum.Timestamp);

                if (lastDistance != currentDistance && lastTime != currentTime)
                {
                    slope = ((currentAltitude - lastAltitude) / (currentDistance - lastDistance));
                    potentialPower = slope * currentSpeed * mass * 9.8F;
                    keneticPower = 0.5F * (mass + options.WheelInertia / (options.WheelRadius * options.WheelRadius)) * ((currentSpeed * currentSpeed) - (lastSpeed * lastSpeed)) / ((float)(currentTime - lastTime));
                    powerAirResistance = ((100.0F - options.DraftingPercentage) / 100.0F) * 0.5F * rho * options.AirDragCofficient * options.AirDragArea * currentSpeed * currentSpeed * currentSpeed;
                    powerRoadResistance = currentSpeed * options.WheelResistanceCoefficient * mass * 9.8F;
                    power = potentialPower + keneticPower + powerAirResistance + powerRoadResistance;

                    // To really work, you need negative power here, and for that to work, you need resistance.
                    if (power < 0F)
                        power = 0F;
                    lastAltitude = currentAltitude;
                    lastDistance = currentDistance;
                    lastSpeed = currentSpeed;
                    lastTime = currentTime;
                    lastPower = power;
                }
                else
                    power = lastPower;


                Field field = new Field(RecordMesg.FieldDefNum.Power, 0x84);
                recordMessage.FieldsList.Add(field);
                recordMessage.SetPower((ushort)power); // Tagg
                encodeDemo.Write(recordMessage);
            }
        }

        #endregion
    }
}
