using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Chronos.Messages;

internal class GameLibrariesStateChangedMessage : ValueChangedMessage<bool>
{
    public GameLibrariesStateChangedMessage() : base(true)
    {
    }
}
