using FFXIVClientStructs.FFXIV.Client.Game.Object;
using SceneObject = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using SceneObjectType = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType;

namespace TargetHighlight.Game;

public static unsafe class GameObjects
{
    /// <summary>
    /// The game object's model if it is a character model (CharacterBase), otherwise 0.
    /// Targets like doors, aetherytes and housing items have other model types without a Tint, which must never be
    /// read or written as a CharacterBase.
    /// </summary>
    public static nint CharacterModelOf(nint gameObject)
    {
        if (gameObject == 0)
            return 0;

        var drawObject = ((GameObject*)gameObject)->DrawObject;
        if (drawObject == null || ((SceneObject*)drawObject)->GetObjectType() != SceneObjectType.CharacterBase)
            return 0;

        return (nint)drawObject;
    }
}
