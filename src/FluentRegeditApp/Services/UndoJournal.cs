using System.Collections.Generic;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public abstract record UndoOp
{
    public abstract void Apply(RegistryEditService edit, RegFileImporter importer);
}

public sealed record RestoreValueOp(
    RegistryRoot Root,
    string SubPath,
    string Name,
    RegistryValueKind Kind,
    object? Data,
    bool Existed) : UndoOp
{
    public override void Apply(RegistryEditService edit, RegFileImporter importer)
    {
        if (Existed && Data is not null)
            edit.SetValue(Root, SubPath, Name, Kind, Data);
        else
            edit.DeleteValue(Root, SubPath, Name);
    }
}

public sealed record RestoreKeyOp(
    RegistryRoot Root,
    string SubPath,
    string RegFileSnapshotPath) : UndoOp
{
    public override void Apply(RegistryEditService edit, RegFileImporter importer)
    {
        importer.Import(RegFileSnapshotPath);
    }
}

public sealed record DeleteValueOp(
    RegistryRoot Root,
    string SubPath,
    string Name) : UndoOp
{
    public override void Apply(RegistryEditService edit, RegFileImporter importer)
    {
        edit.DeleteValue(Root, SubPath, Name);
    }
}

public sealed record DeleteKeyOp(
    RegistryRoot Root,
    string SubPath) : UndoOp
{
    public override void Apply(RegistryEditService edit, RegFileImporter importer)
    {
        edit.DeleteSubKey(Root, SubPath);
    }
}

public sealed class UndoJournal
{
    private const int Capacity = 50;
    private readonly LinkedList<UndoOp> _stack = new();

    public bool CanUndo => _stack.Count > 0;
    public int Count => _stack.Count;

    public void Push(UndoOp op)
    {
        _stack.AddFirst(op);
        while (_stack.Count > Capacity)
            _stack.RemoveLast();
    }

    public void Undo(RegistryEditService edit, RegFileImporter importer)
    {
        if (_stack.First is null) return;
        var op = _stack.First.Value;
        _stack.RemoveFirst();
        op.Apply(edit, importer);
    }

    public void Clear() => _stack.Clear();
}
