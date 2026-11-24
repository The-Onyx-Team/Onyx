using Tmds.DBus.Protocol;

namespace Onyx.Collector.Linux.Sources;

/// <summary>A connection to the user's D-Bus session bus, with the few calls the agent needs.</summary>
internal sealed class SessionBus : IDisposable
{
    private static readonly TimeSpan s_Timeout = TimeSpan.FromSeconds(2);

    private SessionBus(Connection connection) => Connection = connection;

    public Connection Connection { get; }

    /// <returns>Null when there is no session bus (e.g. a service without DBUS_SESSION_BUS_ADDRESS).</returns>
    public static SessionBus? TryConnect()
    {
        if (Address.Session is not { } address)
            return null;
        var connection = new Connection(address);
        try
        {
            connection.ConnectAsync().AsTask().Wait(s_Timeout);
            return new SessionBus(connection);
        }
        catch (Exception)
        {
            connection.Dispose();
            return null;
        }
    }

    /// <summary>Calls a method and reads the reply; throws <see cref="DBusException"/> or a timeout.</summary>
    /// <param name="arguments">Strings, <see cref="uint"/>s and <see cref="int"/>s, matching <paramref name="signature"/>.</param>
    public T Call<T>(string destination, string path, string @interface, string member,
        Func<Message, T> read, string? signature = null, params object[] arguments)
    {
        MessageBuffer message;
        using (var writer = Connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination, path, @interface, member, signature, MessageFlags.None);
            foreach (var argument in arguments)
            {
                switch (argument)
                {
                    case string text:
                        writer.WriteString(text);
                        break;
                    case uint number:
                        writer.WriteUInt32(number);
                        break;
                    case int number:
                        writer.WriteInt32(number);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported D-Bus argument {argument.GetType()}");
                }
            }

            message = writer.CreateMessage();
        }

        var call = Connection.CallMethodAsync(message, (m, _) => read(m), null);
        if (!call.Wait(s_Timeout))
            throw new TimeoutException($"{destination} did not answer {member}");
        return call.Result;
    }

    /// <summary>Takes a well-known name on the bus, so others can call the agent by it.</summary>
    public bool RequestName(string name)
    {
        // 4 = DBUS_NAME_FLAG_DO_NOT_QUEUE; reply 1 = primary owner, 4 = already owner.
        var reply = Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName",
            m => m.GetBodyReader().ReadUInt32(), "su", name, 4u);
        return reply is 1 or 4;
    }

    public void Dispose() => Connection.Dispose();
}
