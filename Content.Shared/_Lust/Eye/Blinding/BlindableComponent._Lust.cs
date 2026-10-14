// Lust added start - Слепота внутри хранилищ предметов
#pragma warning disable IDE0130 // Пространство имён не соответствует структуре папок
namespace Content.Shared.Eye.Blinding.Components;

public sealed partial class BlindableComponent
{
    /// <summary>
    /// Находится ли существо в данный момент внутри предмета-хранилища (StorageComponent).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    [Access(Other = AccessPermissions.ReadWriteExecute)]
    public bool InStorage;
}
// Lust added end
