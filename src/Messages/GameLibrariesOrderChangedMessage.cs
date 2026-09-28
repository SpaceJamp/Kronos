using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Kronos.Messages;


internal class GameLibrariesOrderChangedMessage : ValueChangedMessage<bool>
{
    public GameLibrariesOrderChangedMessage() : base(true)
    {
    }
}
