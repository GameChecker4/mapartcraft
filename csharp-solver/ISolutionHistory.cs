namespace MapartSolving
{
    public interface ISolutionHistory
    {
        public void AddInput(Direction[,] input);

        void EdgeProcessed(StepProcessResult step);
        public void EndPhase(string phaseName);
    }
}