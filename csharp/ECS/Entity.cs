namespace IdleL.ECS;

// https://austinmorlan.com/posts/entity_component_system/#the-entity
// record struct → value equality (the C++ `operator== = default`) + deconstruction for free.
readonly record struct Entity(uint Index, uint Generation)
{
    public const uint InvalidIndex = uint.MaxValue;
}
