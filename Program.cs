using System.Net;
using System.Net.Security;
using System.Threading.Tasks;


class Client
{
    const string chatCode = "chat";
    //const string api = "https://testserver-kaciel13.amvera.io/"; // для публкации
    const string api = "http:localhost:5000/"; // для разработки;

    static async Task Main()
    {
        Console.Write("name?: ");
        var name = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(name)) name = "Nothing";






    }

    




}
