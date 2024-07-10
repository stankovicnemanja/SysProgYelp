//Koristeći principe Reaktivnog programiranja i Yelp Fusion API, implementirati aplikaciju za
//prikaz restorana na određenoj lokaciji (location parametar), koji pripadaju određenoj kategoriji. Prikazati samo one restorane koji
//imaju prosečnu ocenu veću od 4.5, trenutno su otvoreni i imaju broj recenzija koji je veći od 200.
//Sortirati dobijene rezultate u rastući redosled koristeći cenovni rang kao kriterijum. Prikazati samo rezultate kod kojih je cenovni rang 1.

using SysProg3;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using Yelp.Api;
using Yelp.Api.Models;

namespace SysProg3 { 
    public class Business
    {
        public string Name { get; set; }
        public string Price { get; set; }
        public float Rating { get; set; }
        public int ReviewCount { get; set; }
    }
    
    public class BusinessObserver : IObserver<Business>
    {
        private readonly string name;
        public BusinessObserver(string name) 
        {
            this.name = name;
        }
        public void OnNext(Business business)
        {
            if (business.Price != null)
                Console.WriteLine($"{name}: {business.Name} sa cenom: {business.Price}, rating: {business.Rating}, broj recenzija: {business.ReviewCount}");
            else return;
        }
        public void OnError(Exception ex)
        {
            Console.WriteLine($"{name}: Greska: {ex.Message}");
        }
        public void OnCompleted() 
        {
            Console.WriteLine($"{name}: Svi restorani su uspesno vraceni!");
        }
    }

    public class BusinessStream : IObservable<Business>
    {
        private readonly Subject<Business> subject;
        public BusinessStream()
        {
            subject = new Subject<Business>();
        }
        public void GetBusinesses(string location, string categories, float rating, IScheduler scheduler)
        {
            string apiKey = "MoJGVawkVpkn55j9szCdbfxqbhlkpKPIOPJ4gvjNw53VrlvbtgaITpph2DCQdH1h0aQq30qMn6n2aSKND-YdV-nL7YWiROlcxnTqjk1Cnc55cxiToCmOs3ZLcwJ4ZnYx";
            var client = new Client(apiKey);
            Observable.Start(async () =>
            {
                try
                {
                    var request = new SearchRequest
                    {
                        OpenNow = true,
                        Location = location,
                        Categories = categories
                    };
                    var result = await client.SearchBusinessesAllAsync(request);
                    var businesses = result.Businesses.Where(p => p.Rating > rating && p.ReviewCount > 200).ToList();
                    businesses.Sort((p, q) =>
                    {
                        if (p.Price == null && q.Price == null)
                            return 0;
                        if (p.Price == null)
                            return -1;
                        if (q.Price == null)
                            return 1;
                        return p.Price.Length.CompareTo(q.Price.Length);
                    });
                    var businesses2 = businesses.Where(p=>p.Price.Length == 1).ToList();
                    
                    foreach (var business in businesses2)
                    {
                        var newBusiness = new Business
                        {
                            Name = business.Name,
                            Price = business.Price,
                            Rating = business.Rating,
                            ReviewCount = business.ReviewCount,
                        };
                        subject.OnNext(newBusiness);
                    }
                    subject.OnCompleted();
                }
                catch (Exception ex)
                {
                    subject.OnError(ex);
                }
            }, scheduler);
        }
        public IDisposable Subscribe(IObserver<Business> observer)
        {
            return subject.Subscribe(observer);
        }
    }

    public class HttpServer
    {
        private readonly string url;
        private BusinessStream businessStream;
        private IDisposable subscription1;
        private IDisposable subscription2;
        private IDisposable subscription3;

        public HttpServer(string url)
        {
            this.url = url;
        }

        public void Start()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(url);

            listener.Start();
            Console.WriteLine("Server je pokrenut. Osluskivanje nadolazecih zahteva...");

            while(true)
            {
                var context = listener.GetContext();
                Task.Run(() => HandleRequest(context));
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            byte[] buffer;

            if(request.HttpMethod == "GET")
            {
                string location = request.QueryString["location"]!;
                string categories = request.QueryString["categories"]!;
                float rating;

                bool validInput = false;
                if (float.TryParse(request.QueryString["rating"], out rating))
                    validInput = true;
                if (String.IsNullOrEmpty(location) || String.IsNullOrEmpty(categories) || !validInput)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    buffer = Encoding.UTF8.GetBytes("Bad request!");
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else
                {

                    IScheduler scheduler = NewThreadScheduler.Default;

                    businessStream = new BusinessStream();
                    var observer1 = new BusinessObserver("Observer 1");
                    var observer2 = new BusinessObserver("Observer 2");
                    var observer3 = new BusinessObserver("Observer 3");

                    var filteredStream = businessStream;

                    subscription1 = filteredStream.Subscribe(observer1);
                    subscription2 = filteredStream.Subscribe(observer2);
                    subscription3 = filteredStream.Subscribe(observer3);

                    businessStream.GetBusinesses(location, categories, rating, scheduler);

                    response.StatusCode = (int)HttpStatusCode.OK;
                    buffer = Encoding.UTF8.GetBytes("Request received. Processing businesses...");
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.OutputStream.Close();
            }
        }

        public void Stop()
        {
            subscription1!.Dispose();
            subscription2!.Dispose();
            subscription3!.Dispose();
        }
    }

    internal class Program
    {
        public static void Main()
        {
            HttpServer server;
            string url = "http://localhost:8080/";
            server = new HttpServer(url);
            server.Start();
        }
    }
}