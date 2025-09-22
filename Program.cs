using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    const string AdminCode = "chat";
    const string ServerBase = "https://silenceserver-kaciel13.amvera.io/";
    static string localName;
    static int messCount = 2;
    static async Task Main()
    {
        try
        {
            Console.Write("name?: ");
            localName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(localName)) localName = "Nothing";

            using var http = new HttpClient { BaseAddress = new Uri(ServerBase) };
            using var cts = new CancellationTokenSource();

            // Запуск ReadLoop с явным логированием исключений
            var pollTask = Task.Run(async () =>
            {
                try
                {
                    await ReadLoopWithReconnect(http, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine("ReadLoop exception: " + ex);
                }
            });

            Console.WriteLine("Наберите сообщение и нажмите Enter, чтобы отправить.");

            // Если stdin отсутствует (Console.ReadLine() вернёт null), ждем сигнала завершения через /quit
            while (true)
            {
                string? line;
                try
                {
                    Console.SetCursorPosition(0, messCount+10);
                    line = Console.ReadLine();
                }
                catch
                {
                    // В некоторых окружениях stdin недоступен — переходим в ожидание выхода
                    line = null;
                }

                if (line == null)
                {
                    Console.WriteLine("Ввод недоступен. Для завершения закройте приложение.");
                    await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                var messageObj = new
                {
                    message = new
                    {
                        text = line,
                        sentAt = DateTime.UtcNow.ToString("o"),
                        name = localName
                    }
                };
                var json = JsonSerializer.Serialize(messageObj);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    var res = await http.PostAsync($"?code={AdminCode}", content).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Ошибка отправки: {res.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка отправки: " + ex.Message);
                }
            }

            cts.Cancel();
            try { await pollTask.ConfigureAwait(false); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Fatal error: " + ex);
        }
        finally
        {
            // Небольшая пауза чтобы увидеть финальные сообщения при запуске двойным кликом
            await Task.Delay(500);
        }
    }

    // Обёртка для повторного подключения при обрыве
    static async Task ReadLoopWithReconnect(HttpClient http, CancellationToken token)
    {
        var reconnectDelayMs = 1000;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ReadLoop(http, token).ConfigureAwait(false);
                // если ReadLoop завершился без исключения — краткая пауза перед новой попыткой
                await Task.Delay(500, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine("SSE connection error: " + ex.Message);
                await Task.Delay(reconnectDelayMs, token).ConfigureAwait(false);
                // экспоненциальное увеличение паузы (до 30s)
                reconnectDelayMs = Math.Min(30000, reconnectDelayMs * 2);
            }
        }
    }

    static async Task ReadLoop(HttpClient http, CancellationToken token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"?code={AdminCode}");
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        using var stream = await res.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var sb = new StringBuilder();
        while (!token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }

            if (line == null)
            {
                // Сервер закрыл соединение
                break;
            }

            if (line.StartsWith("data:"))
            {
                sb.Append(line.Substring(5).TrimStart());
                sb.Append('\n');
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                var json = sb.ToString().TrimEnd('\n');
                if (!string.IsNullOrEmpty(json))
                {
                    PrintMessageFromJson(json);
                }
                sb.Clear();
            }
            // игнорируем другие SSE-поля
        }
    }

    static void PrintMessageFromJson(string json)
    {
      
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg))
            {
                Console.SetCursorPosition(0, messCount);
                var text = msg.GetProperty("text").GetString() ?? "";
                var sentAtStr = msg.GetProperty("sentAt").GetString() ?? "";
                var name = msg.GetProperty("name").GetString() ?? "";
                if (DateTime.TryParse(sentAtStr, out var sentAt))
                {
                    
                    if(name != localName)
                    {
                        
                        Console.WriteLine($"{sentAt.ToLocalTime():HH:mm} {name} : {text}");
                    }
                    else
                    {
                        Console.WriteLine($">> {sentAt.ToLocalTime():HH:mm} {name} : {text}");
                    }
                    
                }
                else
                {
                    Console.WriteLine($"? {name} : {text}");
                }
            }
            else
            {
                Console.WriteLine("Ответ не содержит поле 'message'.");
            }
            messCount++;
            Console.SetCursorPosition(0, messCount + 9);
            Console.Write("                                                                                       ");
            Console.SetCursorPosition(0, messCount+10);
            Console.Write(">>");
        }
        catch (JsonException)
        {
            Console.WriteLine("Невалидный JSON от сервера.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка разбора сообщения: " + ex.Message);
        }
    }
}