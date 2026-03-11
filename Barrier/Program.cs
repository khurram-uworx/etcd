using System;
using System.Threading;
using System.Threading.Tasks;

namespace Barrier;

public class Program
{
    static async Task driveToBoston(string connectionString, string name, TimeSpan timeToLeaveHome, TimeSpan timeToRoll, TimeSpan timeToGasStation)
    {
        try
        {
            Console.WriteLine("[{0}] Leaving house", name);
            Thread.Sleep(timeToLeaveHome); //let etcd come online and decision time

            var barrier = new Etcd(connectionString);

            // Create barrier if it's the first participant (only one wins)
            await barrier.CreateBarrierAsync((int)timeToRoll.TotalSeconds);

            // Try to enroll in the barrier
            bool enrolled = await barrier.EnrollAsync(name);

            if (!enrolled)
            {
                Console.WriteLine("[{0}] Couldn't join the caravan!", name);
                return;
            }

            Console.WriteLine("[{0}] Going to Boston!", name);
            int participants = await barrier.GetMemberCountAsync();
            Console.WriteLine("[{0}] Caravan has {1} cars!", name, participants);

            // Perform some work
            Thread.Sleep(timeToGasStation);
            Console.WriteLine("[{0}] Arrived at Gas Station", name);

            // Wait for barrier to close (deadline passed or coordinator closed it)
            bool barierClosed = await barrier.WaitForBarrierAsync((int)timeToRoll.TotalSeconds + 10);

            if (!barierClosed)
            {
                Console.WriteLine("[{0}] Barrier wait timed out!", name);
                return;
            }

            // Perform some more work
            Console.WriteLine("[{0}] Leaving for Boston", name);
        }
        catch (Exception ex)
        {
           Console.WriteLine("[{0}] Caravan was cancelled! Going home!", name);
           Console.WriteLine(ex);
        }
    }

    public static async Task Main(string[] args)
    {
        if (args.Length != 4)
            return;

        Console.WriteLine($"[{args[0]}] Waiting 5sec");
        Thread.Sleep(5000);
        Console.WriteLine($"[{args[0]}] timeToLeaveHome:{args[1]}, timeToRoll:{args[2]}, timeToGasStation:{args[3]}");

        await Program.driveToBoston("http://etcd:2379", args[0],
            TimeSpan.FromSeconds(int.Parse(args[1])),   //timeToLeaveHome
            TimeSpan.FromSeconds(int.Parse(args[2])),   //timeToRoll
            TimeSpan.FromSeconds(int.Parse(args[3]))    //timeToGasStation
        );
    }
}
