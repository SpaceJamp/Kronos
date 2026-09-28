using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Chronos.Messages;


internal class GameLibrariesOrderChangedMessage : ValueChangedMessage<bool>
{
    public GameLibrariesOrderChangedMessage() : base(true)
    {
    }
}
