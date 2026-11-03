using System;

namespace RMVC
{
    public abstract class RCommandExecutorBase
    {
        public string Name => GetType().Name;

        internal RCommandExecutorBase() { }

        internal RFacade? Facade { get; set; }

        /// <summary>
        /// Submits one ordered batch of independent roots. Separate calls can overlap.
        /// Prefer one root per initiating event; compose its work inside that command.
        /// </summary>
        protected void Execute(params RCommandBase[] commands)
        {
            if (Facade != null)
            {
                Facade.Execute(commands, GetType().FullName);
                return;
            }

            throw new InvalidOperationException(
                $"Actor '{GetType().FullName}' cannot execute commands without a facade binding. " +
                "Register the actor with RFacade before submitting commands; " +
                "bridges require a registered destination child. Do not call Execute() from its constructor.");
        }
    }
}
