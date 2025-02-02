﻿using System;
namespace RMVC {
    public abstract class RMediator : RCommandExecutorBase {

        public bool IsActive => viewBase != null;

        public IRContract? viewBase { get; set; }
        internal Type viewBaseType { get; private set; }

        internal void UpdateViewBase(IRContract viewBase) {
            OnDisposing();
            this.viewBase = viewBase;

            Initialsed();
        }

        public RMediator(Type view) : base() {
            this.viewBaseType = view;
        }
        private void OnDisposing() {
            if (viewBase != null) {

                Disposing();
                viewBase = null;
            }
        }

        abstract protected void Initialsed();
        abstract protected void Disposing();

    }
}
