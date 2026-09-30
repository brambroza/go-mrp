namespace Mrp.SharedKernel.Domain;

/// <summary>
/// Minimal explicit state machine: a table of allowed (state, trigger) → state transitions.
/// </summary>
/// <typeparam name="TState">Status enum.</typeparam>
/// <typeparam name="TTrigger">Action enum.</typeparam>
public sealed class StateMachine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly Dictionary<(TState State, TTrigger Trigger), TState> _transitions = new();

    /// <summary>Registers a transition and returns the machine for chaining.</summary>
    public StateMachine<TState, TTrigger> Permit(TState from, TTrigger trigger, TState to)
    {
        _transitions[(from, trigger)] = to;
        return this;
    }

    /// <summary>Returns whether <paramref name="trigger"/> may fire in <paramref name="from"/>.</summary>
    public bool CanFire(TState from, TTrigger trigger) => _transitions.ContainsKey((from, trigger));

    /// <summary>Triggers allowed in the given state.</summary>
    public IReadOnlyList<TTrigger> PermittedTriggers(TState from) =>
        _transitions.Keys.Where(k => EqualityComparer<TState>.Default.Equals(k.State, from))
            .Select(k => k.Trigger)
            .ToList();

    /// <summary>Returns the next state or throws <see cref="DomainException"/> when not allowed.</summary>
    public TState Fire(TState from, TTrigger trigger)
    {
        if (!_transitions.TryGetValue((from, trigger), out var to))
        {
            throw DomainException.Conflict(
                "common.invalid_transition",
                $"Action '{trigger}' is not allowed while the document is '{from}'.");
        }

        return to;
    }
}
