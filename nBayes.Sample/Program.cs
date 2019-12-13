﻿using System;
using System.ServiceModel.Syndication;
using System.Web;
using System.Xml;
using System.IO;
using System.Threading.Tasks;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;

using nBayes.Optimization;

namespace nBayes
{
    class Program
    {
        static void Main(string[] args)
        {
            TestClassifier("i just read a new book by Seth Godin called Tribes.  Awesome read. I recommend it!");
        }


        private static void TestBinaryClassifier()
        {
            // Create indexes from twitter using two user supplied terms
            Console.Write("First Index: ");
            string firstSearch = Console.ReadLine();
            var firsttask = CreateIndex(firstSearch);

            Console.Write("Second Index: ");
            string secondSearch = Console.ReadLine();
            var secondtask = CreateIndex(secondSearch);

            Analyzer analyzer = new Analyzer();

            // let the user create a sample text entry that we will classify against the two indices
            Console.Write("Text to Categorize: ");
            var entryToCategorize = Entry.FromString(Console.ReadLine());

            var first = firsttask.Result;
            var second = secondtask.Result;

            CategorizationResult result = analyzer.Categorize(entryToCategorize, first, second);

            Console.Write("And the verdict is ... ");

            switch (result)
            {
                case CategorizationResult.First:
                    Console.WriteLine(firstSearch);
                    break;
                case CategorizationResult.Undetermined:
                    Console.WriteLine("no clue");
                    break;
                case CategorizationResult.Second:
                    Console.WriteLine(secondSearch);
                    break;
            }

            first.Save();
            second.Save();
        }

        private static void TestClassifier(string input)
        {
            Index positiveSentiment = Index.CreateMemoryIndex();
            Index negativeSentiment = Index.CreateMemoryIndex();

            // train the indexes
            positiveSentiment.Add(Entry.FromString("this is awesome?"));
            positiveSentiment.Add(Entry.FromString("i really enjoyed this program"));
            positiveSentiment.Add(Entry.FromString("the new event was GREAT!"));
            positiveSentiment.Add(Entry.FromString("I thought that new phone was ideal, some of the key features blew my mind!"));
            positiveSentiment.Add(Entry.FromString("OK, that's a super idea, lets do it!"));

            negativeSentiment.Add(Entry.FromString("That was terrible! What were you thinking?"));
            negativeSentiment.Add(Entry.FromString("OMG! No way! I can't believe that! Terrible!"));
            negativeSentiment.Add(Entry.FromString("I just got this new phone but wish I hadn't.  It's pretty crap!"));
            negativeSentiment.Add(Entry.FromString("There's no way I'd sign up with them.  Their customer service is terrible!"));
            negativeSentiment.Add(Entry.FromString("That's just rubbish!!!"));

            Analyzer analyzer = new Analyzer();

            Console.WriteLine("Classifying: " + input);

            CategorizationResult result = analyzer.Categorize(Entry.FromString(input), positiveSentiment, negativeSentiment);

            switch (result)
            {
                case CategorizationResult.First:
                    Console.WriteLine("Positive sentiment");
                    break;
                case CategorizationResult.Second:
                    Console.WriteLine("Negative sentiment");
                    break;
                case CategorizationResult.Undetermined:
                    Console.WriteLine("Undecided");
                    break;
            }

        }

        private static Task<FileIndex> CreateIndex(string input)
        {
            var scrubbed = HttpUtility.UrlEncode(input);
            var task = WebHelper.Feed(string.Format("http://search.twitter.com/search.atom?rpp=100&lang=en&q={0}", scrubbed))
            .ContinueWith(r =>
            {
                var feed = r.Result;

                var index = new FileIndex(input + ".xml");
                index.Open();

                foreach (SyndicationItem item in feed.Items)
                {
                    index.Add(Entry.FromString(item.Title.Text));
                }

                return index;
            });

            return task;
        }

        /// <summary>This sample program has 3 options, and will use the the optimizer
        /// to run a test scenario. Two options are picked, and we will simulate user interest in one,
        /// then user sentiment changing to favor the second option.</summary>
        private static void TestOptimizer()
        {
            var optimizer = new Optimizer();

            // define the available options
            Option[] options = new Option[] {
        Option.Named("Orange"),
        Option.Named("Green"),
        Option.Named("White")
    };
            optimizer.Add(options[0]);
            optimizer.Add(options[1]);
            optimizer.Add(options[2]);

            // pick two options, and define when user interest will change
            var firstWinner = options[2]; // white
            var secondWinner = options[1]; // green
            var switchRatio = .35f; // 35% of the way through the test set
            int tries = 100; // the test set

            for (int i = 0; i < tries; i++)
            {
                Option selected = optimizer.Choose().Result; // don't care about asyncrony in this particular example
                Console.WriteLine("Choosing: {0}", selected);

                // decide which chosen option 'the users' will prefer
                bool isFirstBatch = (float)tries * switchRatio > i;
                if (isFirstBatch && selected == firstWinner)
                    firstWinner.IncrementSuccesses();
                else if (!isFirstBatch && selected == secondWinner)
                    secondWinner.IncrementSuccesses();
            }

            Console.WriteLine("\nResults! We expect that ({0}) will have the highest success rate, and ({1}) will be in second place", secondWinner, firstWinner);
            Console.WriteLine("\nThis is the final result after {0} tries\n{1}", tries, optimizer);
        }


        private static void TestBufferedEnumerable()
        {
            var numbers = new int[] { 1, 2, 3, 4, 5, 6 };

            var buffered = new BufferedEnumerable<int>(numbers);

            Console.WriteLine("enumerating now");

            foreach (var item in buffered) Console.WriteLine(item);
        }

    }
    /// <summary>simple web helper to simplify the web request</summary>
    internal static class WebHelper
    {
        public static Task<string> Get(string url)
        {
            var tcs = new TaskCompletionSource<string>();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "nBayes sample client";
            try
            {
                request.BeginGetResponse(iar =>
                {
                    HttpWebResponse response = null;
                    try
                    {
                        response = (HttpWebResponse)request.EndGetResponse(iar);
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var sreader = new StreamReader(response.GetResponseStream());
                            var result = sreader.ReadToEnd();

                            tcs.SetResult(result);
                        }
                        else
                        {
                            tcs.SetResult(string.Empty);
                        }
                    }
                    catch (Exception exc) { tcs.SetException(exc); }
                    finally { if (response != null) response.Close(); }
                }, null);
            }
            catch (Exception exc) { tcs.SetException(exc); }
            return tcs.Task;
        }

        public static Task<SyndicationFeed> Feed(string url)
        {
            var tcs = new TaskCompletionSource<SyndicationFeed>();
            Get(url).ContinueWith(result =>
           {
               TextReader txtreader = new StringReader(result.Result);
               XmlReader reader = XmlReader.Create(txtreader);
               var feed = SyndicationFeed.Load(reader);
               tcs.SetResult(feed);
           });
            return tcs.Task;
        }

        public static Task<T> Json<T>(string url)
        {
            var tcs = new TaskCompletionSource<T>();

            Get(url).ContinueWith(result =>
           {
               try
               {
                   DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                   byte[] bytes = Encoding.UTF8.GetBytes(result.Result);
                   using (var stream = new MemoryStream(bytes))
                   {
                       var deserialized = serializer.ReadObject(stream);

                       tcs.SetResult((T)deserialized);
                   }
               }
               catch (Exception exc) { tcs.SetException(exc); }
           });

            return tcs.Task;
        }
    }
}


