// Copyright RoSchmi 2018, License Apache 2.0
// Version 1.1.1 28.11.2018
// This program is a WPF Application. 
// The App writes Sample Data to Azure Storage Tables.
// The Cloud data are provided in special format, so that they can be graphically visualized with
// the iOS App: Charts4Azure (available in the App Store)
// Created are 5 Tables. Every year new tables are created (TableName + yyyy):
// 1 Table for analog values of 4 sensors (Values must be in the range -40.0 to 140.0, not valid values are epressed as 999.9)
// In the example the values are calculated to display sinus curves
// For a real application you must read an analog sensor an take their values (methods Group readAnalogSensors_1 to Group readAnalogSensors_4

// 4 Tables for On/Off values for 1 digital Input each
// In the example in a timer event the input for each table is toggled, where 
// the timer interval for the Off-State is twice the timer interval of the On-State
// In a real application you must toggle the inputs according to the state of e.g. a GPIO pin
// You can send the states repeatedly, only a change of the state has an effect
// At the end of each day this App automatically sends an Entry with an 'Off' state to the cloud


using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using TableStorage;
using AzureDataSender.Models;
using System.Globalization;

namespace AzureDataSender_WPF
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //DayLightSavingTimeSettings  //not used in this App
        // Europe       
        private static int dstOffset = 60; // 1 hour (Europe 2016)
        private static string dstStart = "Mar lastSun @2";
        private static string dstEnd = "Oct lastSun @3";
        /*  USA
        private static int dstOffset = 60; // 1 hour (US 2013)
        private static string dstStart = "Mar Sun>=8"; // 2nd Sunday March (US 2013)
        private static string dstEnd = "Nov Sun>=1"; // 1st Sunday Nov (US 2013)
        */

        int actTimeDiff = 0;            // Holds timeOffset between local time and UTC

        string TimeOffsetUTCString = "+000";     // Holds timeOffset between local time and UTC as string


        DataContainer dataContainer = new DataContainer(new TimeSpan(0, 15, 0));
        DispatcherTimer getSensorDataTimer;
        DispatcherTimer writeAnalogToCloudTimer;

        DispatcherTimer toggleInputTimer;
       
        OnOffDigitalSensorMgr OnOffSensor_01 = new OnOffDigitalSensorMgr(dstOffset, dstStart, dstEnd, "Burner", false, "OnOffSensor01", "undef", "undef", "undef");
        OnOffDigitalSensorMgr OnOffSensor_02 = new OnOffDigitalSensorMgr(dstOffset, dstStart, dstEnd, "Boiler", false, "OnOffSensor02", "undef", "undef",  "undef");
        OnOffDigitalSensorMgr OnOffSensor_03 = new OnOffDigitalSensorMgr(dstOffset, dstStart, dstEnd, "Pump", false, "OnOffSensor03", "undef", "undef",  "undef");
        OnOffDigitalSensorMgr OnOffSensor_04 = new OnOffDigitalSensorMgr(dstOffset, dstStart, dstEnd, "Heater", false, "OnOffSensor04", "undef", "undef",  "undef");
        
        string connectionString;

        bool AnalogCloudTableExists = false;

        List<string> existingOnOffTables = new List<string>();
       
        #region MainWindow
        public MainWindow()
        {
            InitializeComponent();
            TextBox_OnOff_Table_01.Text = OnOffSensor_01.DestinationTable;
            TextBox_OnOff_Table_02.Text = OnOffSensor_02.DestinationTable;
            TextBox_OnOff_Table_03.Text = OnOffSensor_03.DestinationTable;
            TextBox_OnOff_Table_04.Text = OnOffSensor_04.DestinationTable;

            OnOffSensor_01.digitalOnOffSensorSend += OnOffSensor_01_digitalOnOffSensorSend;
            OnOffSensor_02.digitalOnOffSensorSend += OnOffSensor_02_digitalOnOffSensorSend;
            OnOffSensor_03.digitalOnOffSensorSend += OnOffSensor_03_digitalOnOffSensorSend;
            OnOffSensor_04.digitalOnOffSensorSend += OnOffSensor_04_digitalOnOffSensorSend;
           
            actTimeDiff = Convert.ToInt32(new DateTimeOffset(DateTime.Now).Offset.TotalMinutes);
            TimeOffsetUTCString = actTimeDiff < 0 ? actTimeDiff.ToString("D3") : "+" + actTimeDiff.ToString("D3");

        }
        #endregion

        #region Button_OnOffSensorManager_Start_Clicked
        private void Button_Start_Clicked(object sender, RoutedEventArgs e)
        {
            if (TextBox_Activity.Visibility == Visibility.Visible)
            {
                return;
            }

            TextBox_Activity.Visibility = Visibility.Visible;
            connectionString = "DefaultEndpointsProtocol=https;AccountName=" + TextBox_Account.Text + ";AccountKey=" + TextBox_Key.Text;

            dataContainer.DataInvalidateTime = new TimeSpan(0, 0, int.Parse(this.TextBox_InvalidateInterval.Text));
            getSensorDataTimer = new DispatcherTimer(new TimeSpan(0, 0, int.Parse(this.TextBox_ReadInterval.Text)), DispatcherPriority.Normal, getSensorDataTimer_tick, Dispatcher.CurrentDispatcher);
            writeAnalogToCloudTimer = new DispatcherTimer(new TimeSpan(0, 0, int.Parse(this.TextBox_WriteToCloudInterval.Text)), DispatcherPriority.Normal, writeAnalogToCloudTimer_tick, Dispatcher.CurrentDispatcher);
            toggleInputTimer = new DispatcherTimer(new TimeSpan(0, 0, int.Parse(this.TextBox_OnOffToggleInterval.Text)), DispatcherPriority.Normal, onOffToggleTimer_tick, Dispatcher.CurrentDispatcher);

            AnalogCloudTableExists = false;
            

            OnOffSensor_01.DestinationTable = TextBox_OnOff_Table_01.Text;
            OnOffSensor_02.DestinationTable = TextBox_OnOff_Table_02.Text;
            OnOffSensor_03.DestinationTable = TextBox_OnOff_Table_03.Text;
            OnOffSensor_04.DestinationTable = TextBox_OnOff_Table_04.Text;

            getSensorDataTimer.Start();
            writeAnalogToCloudTimer.Start();
            toggleInputTimer.Start();
            OnOffSensor_01.Start();
            OnOffSensor_02.Start();
            OnOffSensor_03.Start();
            OnOffSensor_04.Start();
        }
        #endregion

        #region Button_OnOffSensorManager_Stop_Clicked
        private void Button_Stop_Clicked(object sender, RoutedEventArgs e)
        {
            getSensorDataTimer.Stop();
            writeAnalogToCloudTimer.Stop();
            toggleInputTimer.Stop();
            OnOffSensor_01.Stop();
            OnOffSensor_02.Stop();
            OnOffSensor_03.Stop();
            OnOffSensor_04.Stop();

            TextBox_Activity.Visibility = Visibility.Collapsed;
        }
        #endregion

        // Change this method for a real application
        #region Timer Event onOffToggleTimer_tick
        private void onOffToggleTimer_tick(object sender, EventArgs e)
        {
            // In this example the digital input states are toggled by a timer event
            // In a real appliction the digital input states must be set e.g. according to the state of a GPIO input

            OnOffSensor_01.Input = !OnOffSensor_01.Input;
            OnOffSensor_02.Input = !OnOffSensor_02.Input;
            OnOffSensor_03.Input = !OnOffSensor_03.Input;
            OnOffSensor_04.Input = !OnOffSensor_04.Input;

            // use different intervals for 'On'- and 'Off' states
            if (OnOffSensor_01.Input == true)
            {
                toggleInputTimer.Interval = new TimeSpan(0, 0, int.Parse(this.TextBox_OnOffToggleInterval.Text) * 2);
            }
            else
            {
                toggleInputTimer.Interval = new TimeSpan(0, 0, int.Parse(this.TextBox_OnOffToggleInterval.Text));
            }
        }
        #endregion

        // Events for 4 digital On/Off Sensors. The events are fired when the input changes its state
        #region Event OnOffSensor_01_digitalOnOffSensorSend
        private async void OnOffSensor_01_digitalOnOffSensorSend(OnOffDigitalSensorMgr sender, OnOffDigitalSensorMgr.OnOffSensorEventArgs e)
        {
            //Console.WriteLine("OnOffSensor_01 Thread-Id: " + e.DestinationTable + " " + Thread.CurrentThread.ManagedThreadId.ToString());
            await WriteOnOffEntityToCloud(e);
        }
        #endregion

        #region Event OnOffSensor_02_digitalOnOffSensorSend
        private async void OnOffSensor_02_digitalOnOffSensorSend(OnOffDigitalSensorMgr sender, OnOffDigitalSensorMgr.OnOffSensorEventArgs e)
        {
            //Console.WriteLine("OnOffSensor_02 Thread-Id: " + e.DestinationTable + " " + Thread.CurrentThread.ManagedThreadId.ToString());
            await WriteOnOffEntityToCloud(e);
        }
        #endregion

        #region Event OnOffSensor_03_digitalOnOffSensorSend
        private async void OnOffSensor_03_digitalOnOffSensorSend(OnOffDigitalSensorMgr sender, OnOffDigitalSensorMgr.OnOffSensorEventArgs e)
        {         
            await WriteOnOffEntityToCloud(e);
        }
        #endregion

        #region Event OnOffSensor_04_digitalOnOffSensorSend
        private async void OnOffSensor_04_digitalOnOffSensorSend(OnOffDigitalSensorMgr sender, OnOffDigitalSensorMgr.OnOffSensorEventArgs e)
        {
            await WriteOnOffEntityToCloud(e);
        }
        #endregion


        // When the timer fires (interval set in the UI) 4 analog inputs are read, the values and a timestamp are stored in the data container
        #region TimerEvent getSensorDataTimer_tick
        private async void getSensorDataTimer_tick(object sender, EventArgs e)
        {
            DateTime actDate = DateTime.Now;
            dataContainer.SetNewAnalogValue(1, actDate, readAnalogSensor_1());
            dataContainer.SetNewAnalogValue(2, actDate, readAnalogSensor_2());
            dataContainer.SetNewAnalogValue(3, actDate, readAnalogSensor_3());
            dataContainer.SetNewAnalogValue(4, actDate, readAnalogSensor_4());
            TextBox_Status_Read.Text = "Read Sensor Data";
            await Task.Delay(1000);
            TextBox_Status_Read.Text = "";
        }
        #endregion


        // When the timer fires (interval set in the UI) an Entity containing the 4 analog values are stored in the Azure Cloud Table
        #region Timer Event writeAnalogToCloudTimer_tick
        private async void writeAnalogToCloudTimer_tick(object sender, EventArgs e)
        {
            writeAnalogToCloudTimer.Stop();
            bool validStorageAccount = false;
            CloudStorageAccount storageAccount = null;
            Exception CreateStorageAccountException = null;
            try
            {
                storageAccount = Common.CreateStorageAccountFromConnectionString(connectionString);
                validStorageAccount = true;
            }
            catch (Exception ex0)
            {
                CreateStorageAccountException = ex0;
            }
            if (!validStorageAccount)
            {
                MessageBox.Show("Storage Account not valid\r\nEnter valid Storage Account and valid Key", "Alert", MessageBoxButton.OK);

                return;
            }
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();


            // Create analog table if not existing           
            CloudTable cloudTable = tableClient.GetTableReference(TextBox_AnalogTable.Text + DateTime.Today.Year);

            if (!AnalogCloudTableExists)
            {
                try
                {
                    await cloudTable.CreateIfNotExistsAsync();
                    AnalogCloudTableExists = true;
                }
                catch
                {
                    Console.WriteLine("Could not create Analog Table with name: \r\n" + cloudTable.Name + "\r\nCheck your Internet Connection.\r\nAction aborted.");
                    TextBox_Status_Write.Text = "Error: Wrong Account or Key";
                    await Task.Delay(1000);
                    TextBox_Status_Write.Text = "";
                    return;
                }
            }
                        

            // Populate Analog Table with Sinus Curve values for the actual day
            cloudTable = tableClient.GetTableReference(TextBox_AnalogTable.Text + DateTime.Today.Year);

            // formatting the PartitionKey this way to have the tables sorted with last added row upmost
            string partitionKey = "Y2_" + DateTime.Today.Year + "-" + (12 - DateTime.Now.Month).ToString("D2");

            DateTime actDate = DateTime.Now;

            // formatting the RowKey (= revereDate) this way to have the tables sorted with last added row upmost
            string reverseDate = (10000 - actDate.Year).ToString("D4") + (12 - actDate.Month).ToString("D2") + (31 - actDate.Day).ToString("D2")
                       + (23 - actDate.Hour).ToString("D2") + (59 - actDate.Minute).ToString("D2") + (59 - actDate.Second).ToString("D2");

            string[] propertyNames = new string[4] { Analog_1.Text, Analog_2.Text, Analog_3.Text, Analog_4.Text };
            Dictionary<string, EntityProperty> entityDictionary = new Dictionary<string, EntityProperty>();
            string sampleTime = actDate.Month.ToString("D2") + "/" + actDate.Day.ToString("D2") + "/" + actDate.Year + " " + actDate.Hour.ToString("D2") + ":" + actDate.Minute.ToString("D2") + ":" + actDate.Second.ToString("D2") + " " + TimeOffsetUTCString;
            //string sampleTime = actDate.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

            entityDictionary.Add("SampleTime", EntityProperty.GeneratePropertyForString(sampleTime));
            for (int i = 1; i < 5; i++)
            {
                double measuredValue = dataContainer.GetAnalogValueSet(i).MeasureValue;
                // limit measured values to the allowed range of -40.0 to +140.0, exception: 999.9 (not valid value)
                if ((measuredValue < 999.89) || (measuredValue > 999.91))  // want to be careful with decimal numbers
                {
                    measuredValue = (measuredValue < -40.0) ? -40.0 : (measuredValue > 140.0 ? 140.0 : measuredValue);
                }
                else
                {
                    measuredValue = 999.9;
                }

                entityDictionary.Add(propertyNames[i - 1], EntityProperty.GeneratePropertyForString(measuredValue.ToString("f1", System.Globalization.CultureInfo.InvariantCulture)));
            }
            DynamicTableEntity sendEntity = new DynamicTableEntity(partitionKey, reverseDate, null, entityDictionary);

            DynamicTableEntity dynamicTableEntity = await Common.InsertOrMergeEntityAsync(cloudTable, sendEntity);


            TextBox_Status_Write.Text = "Data were written to Cloud";
            await Task.Delay(1000);
            TextBox_Status_Write.Text = "";


            writeAnalogToCloudTimer.Start();

        }
        #endregion


        #region Group readAnalogSensors
        private double readAnalogSensor_1()
        {
            // Return the measured value of your sensor here (Range -40.0 - 140.0
            // Only as an example we here return values which draw a sinus curve
            int secondsOnDayElapsed = DateTime.Now.Second + DateTime.Now.Minute * 60 + DateTime.Now.Hour * 60 * 60;
            return Math.Round(2.5f * (double)Math.Sin(Math.PI / 2.0 + (secondsOnDayElapsed * ((4 * Math.PI) / (double)86400))), 1);
        }

        private double readAnalogSensor_2()
        {
            // Return the measured value of your sensor here (Range -40.0 - 140.0
            // Only as an example we here return values which draw a sinus curve
            int secondsOnDayElapsed = DateTime.Now.Second + DateTime.Now.Minute * 60 + DateTime.Now.Hour * 60 * 60;
            return Math.Round(2.5f * (double)Math.Sin(Math.PI / 2.0 + (secondsOnDayElapsed * ((8 * Math.PI) / (double)86400))), 1) + 10;
        }

        private double readAnalogSensor_3()
        {
            // Return the measured value of your sensor here (Range -40.0 - 140.0
            // Only as an example we here return values which draw a sinus curve
            int secondsOnDayElapsed = DateTime.Now.Second + DateTime.Now.Minute * 60 + DateTime.Now.Hour * 60 * 60;
            return Math.Round(2.5f * (double)Math.Sin(Math.PI / 2.0 + (secondsOnDayElapsed * ((12 * Math.PI) / (double)86400))), 1) + 20;
        }
        private double readAnalogSensor_4()
        {
            // Return the measured value of your sensor here (Range -40.0 - 140.0
            // Only as an example we here return values which draw a sinus curve
            int secondsOnDayElapsed = DateTime.Now.Second + DateTime.Now.Minute * 60 + DateTime.Now.Hour * 60 * 60;
            return Math.Round(2.5f * (double)Math.Sin(Math.PI / 2.0 + (secondsOnDayElapsed * ((16 * Math.PI) / (double)86400))), 1) + 30;
        }
        #endregion


        #region Task WriteOnOffEntityToCloud
        async Task WriteOnOffEntityToCloud(OnOffDigitalSensorMgr.OnOffSensorEventArgs e)
        {
            //Console.WriteLine("Common-method Thread-Id: " + e.DestinationTable + " "+ Thread.CurrentThread.ManagedThreadId.ToString());

            bool validStorageAccount = false;
            CloudStorageAccount storageAccount = null;
            Exception CreateStorageAccountException = null;
            try
            {
                storageAccount = Common.CreateStorageAccountFromConnectionString(connectionString);
                validStorageAccount = true;
            }
            catch (Exception ex0)
            {
                CreateStorageAccountException = ex0;
            }
            if (!validStorageAccount)
            {
                MessageBox.Show("Storage Account not valid\r\nEnter valid Storage Account and valid Key", "Alert", MessageBoxButton.OK);

                return;
            }
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable cloudTable = tableClient.GetTableReference(e.DestinationTable + DateTime.Today.Year);

            if (!existingOnOffTables.Contains(cloudTable.Name))
            {
                try
                {
                    await cloudTable.CreateIfNotExistsAsync();
                    existingOnOffTables.Add(cloudTable.Name);
                }
                catch (Exception exc)
                {
                    Console.WriteLine("Could not create On/Off Table with name: \r\n" + cloudTable.Name + "\r\nCheck your Internet Connection.\r\nAction aborted.");
                    return;
                }
            }

            // formatting the PartitionKey this way to have the tables sorted with last added row upmost
            string partitionKey = "Y3_" + DateTime.Today.Year + "-" + (12 - DateTime.Now.Month).ToString("D2");

            DateTime actDate = DateTime.Now;
            // formatting the RowKey this way to have the tables sorted with last added row upmost
            string rowKey = (10000 - actDate.Year).ToString("D4") + (12 - actDate.Month).ToString("D2") + (31 - actDate.Day).ToString("D2")
                       + (23 - actDate.Hour).ToString("D2") + (59 - actDate.Minute).ToString("D2") + (59 - actDate.Second).ToString("D2");

            
            

            //string sampleTime = actDate.Month.ToString("D2") + "/" + actDate.Day.ToString("D2") + "/" + actDate.Year + " " + actDate.Hour.ToString("D2") + ":" + actDate.Minute.ToString("D2") + ":" + actDate.Second.ToString("D2");
            string sampleTime = actDate.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " " + TimeOffsetUTCString;
            TimeSpan tflSend = e.TimeFromLastSend;
            string timeFromLastSendAsString = tflSend.Days.ToString("D3") + "-" + tflSend.Hours.ToString("D2") + ":" + tflSend.Minutes.ToString("D2") + ":" + tflSend.Seconds.ToString("D2");

            string onTimeDayAsString = e.OnTimeDay.ToString(@"ddd\-hh\:mm\:ss", CultureInfo.InvariantCulture);

            Dictionary<string, EntityProperty> entityDictionary = new Dictionary<string, EntityProperty>();
            entityDictionary.Add("SampleTime", EntityProperty.GeneratePropertyForString(sampleTime));
            entityDictionary.Add("ActStatus", EntityProperty.GeneratePropertyForString(e.ActState ? "Off" : "On"));
            entityDictionary.Add("LastStatus", EntityProperty.GeneratePropertyForString(e.OldState ? "Off" : "On"));
            entityDictionary.Add("TimeFromLast", EntityProperty.GeneratePropertyForString(timeFromLastSendAsString));
            entityDictionary.Add("OnTimeDay", EntityProperty.GeneratePropertyForString(onTimeDayAsString));

            DynamicTableEntity dynamicTableEntity = await Common.InsertOrMergeEntityAsync(cloudTable, new DynamicTableEntity(partitionKey, rowKey, null, entityDictionary));
        }
        #endregion
       
    }
}

