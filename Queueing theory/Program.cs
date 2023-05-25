using System;
using System.Threading;
using System.Diagnostics;

namespace Laba7
{
    class Program
    {
        static void Main(string[] args)
        {
            double intensity_request = 10; // интенсивоность потока запросов
            double intensity_service = 2; // интенсивность потока обслуживания
            int clients_count = 200;
            int thread_count = 5;
            int request_interval = (int)Math.Round(1000.0 / intensity_request);
            int service_time = (int)Math.Round(1000.0 / intensity_service);

            //Запуск 
            Server server = new Server(thread_count, service_time);
            Client client = new Client(server);

            server.Start();
            for (int id = 1; id <= clients_count; id++)
            {
                
                client.send(id);
                Thread.Sleep(request_interval);
            }
            server.Stop(); 

            IndicatorsQueingSystem theoretical_result = Calculate(intensity_request, intensity_service, thread_count);
            IndicatorsQueingSystem actual_result;
            actual_result.probability_of_system_downtime = (double)server.downtime / server.work_time;
            actual_result.probability_of_system_failure = (double)server.rejectedCount / server.requestCount;
            actual_result.relative_throughput = (double)server.proccessedCount / server.requestCount;
            actual_result.absolute_throughput = server.proccessedCount / ((double)server.work_time / 1000);
            actual_result.average_number_of_busy_threads = (double)server.sum_time_of_busy_thread / server.work_time;


            //Вывод информации
            Console.WriteLine("Входные параметры:\n\tИнтенсивность потока заявок: {0}\n\tИнтенсивность потока обслуживания: {1}\n\tКоличество каналов: {2}\n", intensity_request, intensity_service, thread_count);

            Console.WriteLine("Результаты запуска:");
            Console.WriteLine("\tВсего заявок: {0}", server.requestCount);
            Console.WriteLine("\tОбработано заявок: {0}", server.proccessedCount);
            Console.WriteLine("\tОтклонено заявок: {0}\n", server.rejectedCount);

            Console.WriteLine("|                                      | Теоретические результаты | Практические результаты  |    Абсолютная разница    |");
            Console.WriteLine("|--------------------------------------|--------------------------|--------------------------|--------------------------|");
            Console.WriteLine("| Вероятность простоя системы          | {0,24:0.0000000000000} | {1,24:0.0000000000000} | {2,24:0.0000000000000} |",
                theoretical_result.probability_of_system_downtime, actual_result.probability_of_system_downtime,
                Math.Abs(theoretical_result.probability_of_system_downtime - actual_result.probability_of_system_downtime));
            Console.WriteLine("| Вероятность отказа системы           | {0,24:0.0000000000000} | {1,24:0.0000000000000} | {2,24:0.0000000000000} |",
                theoretical_result.probability_of_system_failure, actual_result.probability_of_system_failure,
                Math.Abs(theoretical_result.probability_of_system_failure - actual_result.probability_of_system_failure));
            Console.WriteLine("| Относительная пропускная способность | {0,24:0.0000000000000} | {1,24:0.0000000000000} | {2,24:0.0000000000000} |",
                theoretical_result.relative_throughput, actual_result.relative_throughput,
                Math.Abs(theoretical_result.relative_throughput - actual_result.relative_throughput));
            Console.WriteLine("| Абсолютная пропускная способность    | {0,24:0.0000000000000} | {1,24:0.0000000000000} | {2,24:0.0000000000000} |",
                theoretical_result.absolute_throughput, actual_result.absolute_throughput,
                Math.Abs(theoretical_result.absolute_throughput - actual_result.absolute_throughput));
            Console.WriteLine("| Среднее число занятых каналов        | {0,24:0.0000000000000} | {1,24:0.0000000000000} | {2,24:0.0000000000000} |",
                theoretical_result.average_number_of_busy_threads, actual_result.average_number_of_busy_threads,
                Math.Abs(theoretical_result.average_number_of_busy_threads - actual_result.average_number_of_busy_threads));

        }

        static IndicatorsQueingSystem Calculate(double intensity_request, double intensity_service, int thread_count)
        {
            IndicatorsQueingSystem result;
            double reduced_intencity = intensity_request / intensity_service;
            double sum = 1;
            double pow = 1;
            double factorial=1;
            for (int i = 1; i <= thread_count; i++)
            {
                factorial *= i;
                pow *= reduced_intencity;
                sum += pow / factorial;
            }
            result.probability_of_system_downtime = Math.Pow(sum, -1);
            result.probability_of_system_failure = pow / factorial * result.probability_of_system_downtime;
            result.relative_throughput = 1 - result.probability_of_system_failure;
            result.absolute_throughput = intensity_request * result.relative_throughput;
            result.average_number_of_busy_threads = result.absolute_throughput / intensity_service;
            return result;
        }


    }
    struct IndicatorsQueingSystem
    {
        public double probability_of_system_downtime; //вероятность простоя системы
        public double probability_of_system_failure; //вероятность отказа системы
        public double relative_throughput; //относительная пропускная способность
        public double absolute_throughput; //абсолютная пропускная способность
        public double average_number_of_busy_threads; //среднее число занятых каналов
    }

    struct PoolRecord
    {
        public Thread thread;
        public bool in_use;
    }

    class Server
    {
        private PoolRecord[] pool;// пул потоков
        private object threadLock = new object();
        private int poolSize;//размер пула потоков
        private int serviceTime;//время обработки клиента
        public int requestCount = 0;//количество запросов
        public int proccessedCount = 0;//количество принятых запросов
        public int rejectedCount = 0;//количество отклонненых запросов
        private Stopwatch downtime_sw = new Stopwatch();//секундомер для фиксирования времени простоя сервера
        private Stopwatch work_sw = new Stopwatch();//секундомер для фиксирования времени работы сервера
        private Stopwatch busy_thread_sw = new Stopwatch();//секундомер для фиксирования времени работы постоянного количества потоков
        public long downtime = 0;//время простоя сервера
        public long work_time = 0;//время работы сервера
        private int busy_thread_count=0;//количество занятых в текущий момент потоков
        public long sum_time_of_busy_thread=0;//сумма произведений количества занятых потоков и времени работы этого количества потоков (нужно для среднего числа занятых каналов)
        public Server(int max_clien_count, int service_time)
        {
            poolSize = max_clien_count;
            serviceTime = service_time;
            pool = new PoolRecord[poolSize];
        }

        public void proc(object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                requestCount++;
                downtime_sw.Stop();
                for (int i = 0; i < poolSize; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        proccessedCount++;
                        sum_time_of_busy_thread += busy_thread_count * busy_thread_sw.ElapsedMilliseconds;
                        busy_thread_count++;
                        busy_thread_sw.Restart();
                        return;
                    }
                }
                rejectedCount++;
            }
        }

        public void Answer(object arg)
        {
            int id = (int)arg;
            Thread.Sleep(serviceTime);
            for (int i = 0; i < poolSize; i++)
            {
                if (pool[i].thread == Thread.CurrentThread)
                {
                    pool[i].in_use = false;
                    sum_time_of_busy_thread += busy_thread_count * busy_thread_sw.ElapsedMilliseconds;
                    busy_thread_count--;
                    busy_thread_sw.Restart();
                }
                
            }
            if (busy_thread_count==0) downtime_sw.Start();
        }

        public void Start()
        {
            downtime_sw.Start();
            work_sw.Start();
            busy_thread_sw.Start();
        }
        public void Stop()
        {
            downtime_sw.Stop();
            work_sw.Stop();
            busy_thread_sw.Stop();
            downtime = downtime_sw.ElapsedMilliseconds;
            work_time = work_sw.ElapsedMilliseconds;
            sum_time_of_busy_thread += busy_thread_count * busy_thread_sw.ElapsedMilliseconds;
        }
    }

    class Client
    {
        private Server server;
        public Client(Server server)
        {
            this.server = server;
            this.request += server.proc;
        }

        public void send(int id)
        {
            procEventArgs args = new procEventArgs();
            args.id = id;
            OnProc(args);
        }

        protected virtual void OnProc(procEventArgs e)
        {
            EventHandler<procEventArgs> handler = request;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        public event EventHandler<procEventArgs> request;
    }

    public class procEventArgs: EventArgs
    {
        public int id { get; set; }
    }

}
