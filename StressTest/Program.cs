// See https://aka.ms/new-console-template for more information
using StressTest;

var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettingReader>(System.IO.File.ReadAllText("appsettings.json"));
if (settings == null)
{
    Console.WriteLine("No settings found.");
    return;
}
Console.WriteLine($"Detected {settings.Tests.Length} Test(s) to Run in {settings.Threads} Threads {(settings.Paralell ? "Paralell" : "Sequencially")}.");

object CancelWork = false;

List<TestRunner> runnerList = new List<TestRunner>();

if (settings.Paralell)
{    
    foreach (var item in settings.Tests)
    {
        TestRunner trunner = new TestRunner();
        runnerList.Add(trunner);
        ThreadPool.QueueUserWorkItem(trunner.QueueWork, new object[] { item, CancelWork, settings.Threads });
    }
    Console.WriteLine("Press any key to conclude the test.");
    while (!Console.KeyAvailable)
    {
        System.Threading.Thread.Sleep(10);
    }
    CancelWork = true;
    
}
else
{
    foreach (var item in settings.Tests)
    {
        CancelWork = false;
        TestRunner trunner = new TestRunner();
        runnerList.Add(trunner);
        ThreadPool.QueueUserWorkItem(trunner.QueueWork, new object[] { item, CancelWork, settings.Threads });
        Console.WriteLine("Press any key to conclude the test.");
        while(!Console.KeyAvailable)
        {
            System.Threading.Thread.Sleep(10);
        }
        CancelWork = true;
    }
}

foreach(var runner in runnerList)
{
    Console.WriteLine($"{runner.endPoint} was ran {runner.CallTimes} times for {runner.TotalRunTime.TotalMinutes} Minutes and had {runner.PercentSuccess.ToString("##0.00")}% success with average {runner.AverageTime} ms average call time.");
}