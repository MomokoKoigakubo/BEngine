namespace IdleL.Molang;

class MolangContext
{
    public float AnimTime;
    public float LifeTime;
    public float DeltaTime;
    public Dictionary<string, float> Variables = new();   // variable./v./temp./t. lookups
}
