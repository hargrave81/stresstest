using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace StressTest
{
    internal class TestRunner      
    {
        internal static HttpClient client;

        public List<ApiResult> results = new List<ApiResult>();

        public decimal AverageTime => results.Average(t => t.CallTime);
        public decimal PercentSuccess => (decimal)((float)results.Count(t => t.StatusCode < 300 || (exclude404 ? t.StatusCode == 404 : t.StatusCode == 1)) / (float)results.Count() * 100);

        public TimeSpan TotalRunTime = TimeSpan.Zero;

        public int CallTimes => results.Count;

        public string endPoint { get; set; }
        private string token { get; set; }
        private string[] query { get; set; }
        private string[] payload { get; set; }
        
        private int entryToSend = 0;

        private int currentCount = 0;

        private object EntryLock = new object();

        private System.Diagnostics.Stopwatch stopWatch;
        internal bool exclude404 { get; set; }
        public TestRunner()
        {
            client = new HttpClient();
            stopWatch = new System.Diagnostics.Stopwatch();
        }

        public ApiResult CallAPIQuery(string endPoint, string Query, string token)
        {
            var result = new ApiResult() {EndPoint = endPoint };
            var timer = new System.Diagnostics.Stopwatch();            
            var message = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(endPoint), Query).ToString());
            if(!string.IsNullOrEmpty(token))
            {
                // add the token
                message.Headers.Add("Authorization", "Bearer " + token);
            }
            try
            {
                timer.Start();
                var callResult = AsyncHelpers.RunSync(() => client.SendAsync(message));
                timer.Stop();
                result.StatusCode =(int)callResult.StatusCode;
            }
            catch(Exception ex)
            {
                timer.Stop();
                result.StatusCode = 500;                
            }
            result.CallTime = timer.ElapsedMilliseconds;
            return result;
        }

        public ApiResult CallAPIPayload(string endPoint, string payLoad, string token)
        {
            var result = new ApiResult() { EndPoint = endPoint };
            var timer = new System.Diagnostics.Stopwatch();
            var message = new HttpRequestMessage(HttpMethod.Post, endPoint);
            message.Content = JsonContent.Create(payLoad);
            if (!string.IsNullOrEmpty(token))
            {
                // add the token
                message.Headers.Add("Authorization", "Bearer " + token);
            }
            try
            {
                timer.Start();
                var callResult = AsyncHelpers.RunSync(() => client.SendAsync(message));
                timer.Stop();
                result.StatusCode = (int)callResult.StatusCode;
            }
            catch (Exception ex)
            {
                timer.Stop();
                result.StatusCode = 500;
            }
            result.CallTime = timer.ElapsedMilliseconds;
            return result;
        }

        public void RunTestQuery(int threads, string endPoint, string[] query, string token, string clientId, string secret, string scope, string tokenUri, bool cancellationToken)
        {
            if(string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(tokenUri))
            {
                // fetch token first
                this.token = GetToken(clientId, secret, scope, tokenUri).AccessToken;
            }
            else
            {
                this.token = token;
            }
            this.endPoint = endPoint;
            this.query = query;
            RunSeveralTests(threads, cancellationToken);
        }

        public void RunTestObjects(int threads, string endPoint, object[] objects, string token, string clientId, string secret, string scope, string tokenUri, bool cancellationToken)
        {
            if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(tokenUri))
            {
                // fetch token first
                this.token = GetToken(clientId, secret, scope, tokenUri).AccessToken;
            }
            else
            {
                this.token = token;
            }
            this.endPoint = endPoint;            
            this.payload = objects.Select(t => System.Text.Json.JsonSerializer.Serialize(t)).ToArray();
            RunSeveralTests(threads, cancellationToken);
        }

        /// <summary>
        /// Main Entry Point
        /// </summary>
        /// <param name="workToQueue"></param>
        public void QueueWork(object workToQueue)
        {
            object cancellationToken = (workToQueue as object[])[1];
            Test test = (Test)(workToQueue as object[])[0];
            int threads = (int)(workToQueue as object[])[2];
            if (test.PayLoadQuery != null && test.PayLoadQuery.Length > 0)
            {
                RunTestQuery(threads, test.EndPoint, test.PayLoadQuery, test.Token, test.ClientId, test.Secret, test.Scope, test.TokenUri, (bool)cancellationToken);
            }
            else
            {
                RunTestObjects(threads, test.EndPoint, test.PayLoadObjects, test.Token, test.ClientId, test.Secret, test.Scope, test.TokenUri, (bool)cancellationToken);
            }            
        }

        private void RunSeveralTests(int threads, bool cancellationToken)
        {
            var timer = new System.Diagnostics.Stopwatch();
            timer.Start();
            stopWatch.Start();
            for (int thread = 0; thread < threads;thread ++)
            {
                ThreadPool.QueueUserWorkItem(BackgroundTask, cancellationToken);
            }
            while(!cancellationToken)
            {
                System.Threading.Thread.Sleep(10);
                TotalRunTime = new TimeSpan(timer.ElapsedTicks);
            }
            stopWatch.Stop();
            timer.Stop();
            
        }

        private void BackgroundTask(Object stateInfo)
        {
            bool cancelToken = (bool)stateInfo;
            while (!cancelToken)
            {
                bool logEntry = false;
                int entryThatIsend = 0;
                lock (EntryLock)
                {
                    entryThatIsend = entryToSend;
                    entryToSend++;
                    if(entryToSend >= (this.payload == null ? this.query.Length : this.payload.Length))
                    {
                        entryToSend = 0;
                        logEntry = true;
                    }
                }
                if(logEntry)
                {
                    stopWatch.Stop();
                    decimal seconds = stopWatch.ElapsedMilliseconds / 1000;
                    stopWatch.Reset();
                    stopWatch.Start();
                    var tempEntries = results.Skip(currentCount).ToList();
                    float callsMade = results.Count - currentCount;
                    currentCount = results.Count;
                    decimal callsPerSecond = (decimal)callsMade / seconds;
                    decimal avg = tempEntries.Average(t => t.CallTime);
                    decimal success = (decimal)((float)tempEntries.Count(t => t.StatusCode < 300 || (exclude404 ? t.StatusCode == 404 : t.StatusCode == 1)) / (float)tempEntries.Count() * 100);
                    Console.WriteLine($"Current Progress '...{endPoint.Substring(endPoint.Length - 20)}' - {avg.ToString("##0.00")}ms average   {success.ToString("##0.00")}% success with {callsPerSecond.ToString("##0")} calls/s");
                }
                if (this.payload == null || this.payload.Length == 0)
                {
                    // send query
                    var result = CallAPIQuery(endPoint, this.query[entryThatIsend], token);
                    results.Add(result);
                }
                else
                {
                    // send object
                    var result = CallAPIPayload(endPoint, this.payload[entryThatIsend], token);
                    results.Add(result);
                }
            }
        }

        

        public Token GetToken(string clientId, string secret, string scope, string tokenUri)
        {
            return Token.GetToken(tokenUri, clientId, secret, scope.Split(" "));
        }
    }
}
