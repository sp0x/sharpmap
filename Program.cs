using System;

namespace sharpmap
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var communicator = new FileClient("MemoryMappedShare", 4096);

            // This process reads data that begins in the position 2000 and writes starting from the position 0.
            communicator.ReadPosition = 2000;
            communicator.WritePosition = 0;

            // Creates an handler for the event that is raised when data are available in the
            // MemoryMappedFile.
            communicator.DataReceived += (x, e) =>
            {
                e = e;
                x = x;
            };
            communicator.Write("x123456");
            communicator.StartReader();
            Console.ReadLine();
        }
    }
}
