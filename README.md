# RMVC

> **Explicit architecture. Portable applications.**

RMVC is a lightweight, command-driven MVC architecture for C# applications.

It is designed to keep **application behaviour independent of presentation technology**, using explicit **Models**, **View Contracts**, **Mediators** and **Commands** rather than relying heavily on UI-framework binding conventions.

The same application core can therefore be reused across multiple presentation technologies with minimal change.

This repository includes implementations of the same sample application using:

* Avalonia
* Blazor
* Electron
* Windows Forms
* .NET MAUI
* Unity
* WPF

All share the same core application architecture.

---

## Why RMVC?

UI frameworks differ enormously in how they handle presentation.

WPF has XAML and data binding.
WinForms is primarily event-driven.
Blazor uses components.
Unity uses GameObjects and MonoBehaviours.
Avalonia has its own presentation model.

Yet the underlying application behaviour often changes very little.

RMVC attempts to make that distinction explicit.

Instead of allowing application architecture to become coupled to the conventions of a particular UI framework:

* Views expose small C# contracts
* Mediators connect those views to the application
* Commands represent application actions
* Models own application state
* The Facade coordinates the application

The result is a core application layer that can remain largely unchanged when the presentation technology changes.

---

# Philosophy

RMVC favours explicit application flow over implicit framework behaviour.

Its core principles are:

* Explicit communication over implicit binding
* Small focused classes over large presentation objects
* Command-driven application behaviour
* Clear ownership of application state
* Minimal dependency on UI frameworks
* Portability across presentation technologies

A central question in RMVC is:

> **What application action is taking place?**

That action usually becomes a Command.

---

# Architecture

RMVC is best described as a **command-driven variation of MVC**.

```text
              User
                │
                ▼
          +-----------+
          |   View    |
          +-----------+
                │
          View Contract
                │
                ▼
          +-----------+
          | Mediator  |
          +-----------+
                │
                ▼
          +-----------+
          |  Command  |
          +-----------+
                │
                ▼
          +-----------+
          |   Model   |
          +-----------+
```

The Mediator occupies the controller boundary, but unlike a traditional controller it should contain very little application behaviour.

Its primary responsibility is to translate presentation events into application operations.

Those operations are represented explicitly as Commands.

Models own the application state.

---

# Core Components

## RFacade

`RFacade` is the application composition root and coordinator.

It is responsible for:

* Registering Models
* Registering Mediators
* Executing Commands
* Managing application startup
* Providing access to registered application components

A concrete application facade defines the application's composition.

```csharp
public class AppFacade : RFacade
{
    protected override RModel[] RegisterModels()
    {
        return new RModel[]
        {
            new CounterModel()
        };
    }

    protected override RMediator[] RegisterMediators()
    {
        return new RMediator[]
        {
            new CounterMediator(typeof(ICounterView))
        };
    }

    protected override RCommandBase RegisterStartupCommand()
    {
        return new StartupCmd();
    }
}
```

---

## Models

Models own application state and domain behaviour.

They are independent of the concrete presentation technology.

```csharp
public class CounterModel : RModel
{
    public int CounterCount { get; set; }

    protected internal override void Initialise()
    {
        CounterCount = 0;
    }
}
```

A Model should not need to know whether the application is being presented through WPF, Avalonia, Blazor, Unity or another UI framework.

---

## View Contracts

Views communicate with RMVC through ordinary C# interfaces.

```csharp
public interface ICounterView : IRContract
{
    event Action<int> SetCounterEvt;

    void SetCounter(int value);
}
```

The contract describes what the application requires from the View without depending on the concrete UI implementation.

A WPF UserControl, Avalonia View, Blazor component or Unity object can therefore implement the same application-facing contract.

---

## Mediators

Mediators form the boundary between presentation and application behaviour.

They typically:

* Subscribe to View events
* Translate UI interaction into Commands
* Coordinate View updates
* Isolate framework-specific presentation behaviour

```text
View Event
    │
    ▼
Mediator
    │
    ▼
Command
```

A Mediator should remain relatively small.

Application behaviour belongs in Commands and Models rather than accumulating inside the presentation layer.

---

## Commands

Commands represent application actions.

Examples might include:

* Save document
* Open project
* Load games
* Update counter
* Import data
* Refresh content

A Command expresses one operation explicitly.

```csharp
internal class SetCounterValueCmd : RCommand
{
    private readonly int counterCount;

    public SetCounterValueCmd(int counterCount)
    {
        this.counterCount = counterCount;
    }

    protected override void Run()
    {
        if (AppFacade.Instance?.CounterModel != null)
            AppFacade.Instance.CounterModel.CounterCount = counterCount;
    }
}
```

Commands may also execute other Commands, allowing larger operations to be composed from smaller units of behaviour.

This makes application flow explicit and keeps operational logic out of the UI.

---

## Async Commands

`RCommandAsync` extends the same architecture to asynchronous operations.

It supports:

* `async` / `await`
* Cancellation
* Progress reporting
* Nested Commands
* Progress propagation
* Long-running application operations

This allows asynchronous workflows to remain part of the application's command architecture rather than being managed directly by individual Views.

---

# Control Flow

A typical user interaction might look like this:

```text
User clicks button
       │
       ▼
View raises event
       │
       ▼
Mediator receives event
       │
       ▼
Mediator executes Command
       │
       ▼
Command modifies Model
       │
       ▼
Mediator updates View
```

The important distinction is that each stage has a clear responsibility.

The View handles presentation.

The Mediator handles presentation coordination.

The Command performs the application action.

The Model owns the state.

---

# Cross-Platform by Design

One of RMVC's main goals is presentation portability.

The repository demonstrates the same shared application core behind multiple UI technologies.

```text
Sample/
│
├── RMVCApp.Sample.Core
│   ├── Command
│   ├── Mediator
│   ├── Model
│   └── Shared
│
├── RMVCApp.Sample.Avalonia
├── RMVCApp.Sample.Blazor
├── RMVCApp.Sample.Electron
├── RMVCApp.Sample.Forms
├── RMVCApp.Sample.Maui
├── RMVCApp.Sample.Unity
└── RMVCApp.Sample.WPF
```

The Core project contains the application architecture.

Each presentation project implements the required View Contracts using its own UI technology.

The central idea is simple:

> **Change the presentation layer without rewriting the application layer.**

---

# Is RMVC Really MVC?

RMVC is not intended to be textbook MVC.

It preserves the core MVC separation between presentation, application coordination and state, but introduces Commands as first-class architectural components.

Traditional MVC is commonly represented as:

```text
View
  ↓
Controller
  ↓
Model
```

RMVC instead uses:

```text
View
  ↓
Mediator
  ↓
Command
  ↓
Model
```

The Mediator effectively occupies the controller boundary, while Commands contain the discrete application operations that might otherwise accumulate inside controller methods.

For that reason, RMVC is best described as:

> **A command-driven MVC architecture with a mediator-based presentation boundary.**

---

# Relationship to MVVM

RMVC was partly motivated by experience with MVVM-based application development.

MVVM is a strong fit for many applications, particularly where declarative binding is desirable.

RMVC makes a different set of trade-offs.

Where MVVM often relies on:

* Binding expressions
* DataContext
* Property change notification
* Presentation-focused ViewModels
* Framework conventions

RMVC generally prefers:

* C# interfaces
* Events
* Explicit method calls
* Commands
* Mediators
* Explicit application flow

Neither approach is universally better.

RMVC is intended for developers and applications where **explicit control flow, UI independence and architectural portability** are more valuable than extensive presentation binding.

---

# Why Commands?

Commands are more than a convenience in RMVC.

They provide a clear vocabulary for application behaviour.

Instead of behaviour being distributed across:

* Event handlers
* ViewModels
* Controllers
* Code-behind
* UI lifecycle callbacks

an application operation can be represented by a named object.

```text
LoadProjectCmd

SaveSettingsCmd

RefreshLibraryCmd

DeleteItemCmd

StartImportCmd
```

This makes it easier to reason about:

* What the application can do
* Where a behaviour is implemented
* Which operations can be reused
* Which operations can be composed
* Which operations are asynchronous

The Command layer therefore becomes the behavioural centre of the application.

---

# Getting Started

Clone the repository:

```bash
git clone https://github.com/seanstardev/RMVC.git
```

Open the sample solution:

```text
RMVCApp.Sample.sln
```

A useful way to understand the architecture is to compare the shared Core project with two different presentation projects.

For example:

```text
RMVCApp.Sample.Core

RMVCApp.Sample.WPF

RMVCApp.Sample.Avalonia
```

The Models, Commands and Mediators remain in the shared application Core while each presentation project provides its own View implementations.

---

# Building an RMVC Application

A typical RMVC application contains:

1. An `RFacade` implementation
2. One or more `RModel` classes
3. View Contracts derived from `IRContract`
4. `RMediator` implementations connecting Views to the application
5. `RCommand` or `RCommandAsync` classes representing application operations

A simplified project structure might look like:

```text
MyApplication.Core/
│
├── Commands/
│   ├── LoadDataCmd.cs
│   └── SaveDataCmd.cs
│
├── Models/
│   └── ApplicationModel.cs
│
├── Mediators/
│   └── MainMediator.cs
│
├── Contracts/
│   └── IMainView.cs
│
└── AppFacade.cs
```

The presentation project then implements `IMainView` using the chosen UI framework.

---

# Design Goals

RMVC aims to:

* Keep application behaviour independent of UI frameworks
* Make control flow visible and explicit
* Reduce presentation-layer coupling
* Encourage small, focused classes
* Make application behaviour easy to locate
* Support portable application cores
* Support synchronous and asynchronous workflows
* Remain lightweight
* Avoid forcing a large supporting ecosystem onto the application

---

# What RMVC Is Not

RMVC is not intended to be:

* A dependency injection container
* A replacement for every UI framework feature
* A reactive framework
* A binding framework
* A strict implementation of academic MVC
* A complete application platform

It is deliberately small.

Its purpose is to provide a clear architectural skeleton around which an application can be built.

Use the presentation framework for presentation.

Use RMVC for application structure.

---

# Framework Structure

The main RMVC types are:

| Type            | Responsibility                           |
| --------------- | ---------------------------------------- |
| `RFacade`       | Application composition and coordination |
| `RModel`        | Application state and domain behaviour   |
| `RMediator`     | Presentation/application boundary        |
| `RCommand`      | Synchronous application operation        |
| `RCommandAsync` | Asynchronous application operation       |
| `IRContract`    | Base contract implemented by Views       |
| `RTracker`      | Async command tracking and cancellation  |
| `RProgress`     | Command progress representation          |

---

# Target Frameworks

RMVC currently targets:

```text
.NET Standard 2.1
.NET 8.0
```

---

# Project Status

RMVC is an evolving architecture project developed from practical experience building applications across desktop, web, mobile and game-engine environments.

The project is intentionally compact and opinionated.

It is intended both as a usable lightweight framework and as an exploration of explicit, command-driven application architecture in C#.

Feedback, experimentation and contributions are welcome.

---

# License

RMVC is released under the MIT License.
