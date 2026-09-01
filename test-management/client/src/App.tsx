import { NavLink, Route, Routes } from "react-router-dom";
import { useHealth } from "./hooks/useHealth";
import { DashboardPage } from "./pages/DashboardPage";
import { RunsPage } from "./pages/RunsPage";
import { RunDetailPage } from "./pages/RunDetailPage";
import { RunComparePage } from "./pages/RunComparePage";
import { TestsPage } from "./pages/TestsPage";
import { TestDetailPage } from "./pages/TestDetailPage";
import { EnvironmentsPage } from "./pages/EnvironmentsPage";
import { TestDataPage } from "./pages/TestDataPage";

const navItems = [
  { to: "/", label: "Dashboard", end: true },
  { to: "/runs", label: "Runs" },
  { to: "/tests", label: "Tests" },
  { to: "/environments", label: "Environments" },
  { to: "/test-data", label: "Test data" },
];

const HEALTH_LABEL: Record<string, string> = {
  ok: "API OK",
  checking: "Checking…",
  error: "API OFFLINE",
};

function App() {
  const health = useHealth();

  return (
    <div>
      <header className="app-header">
        <strong className="app-title">OffenderWatch — Test Management</strong>
        <nav className="app-nav">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `app-nav-link${isActive ? " app-nav-link-active" : ""}`}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <span className={`health-badge health-${health}`} title="API connectivity">
          <span className="health-dot" aria-hidden="true" />
          {HEALTH_LABEL[health]}
        </span>
      </header>

      <main>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/runs" element={<RunsPage />} />
          <Route path="/runs/compare" element={<RunComparePage />} />
          <Route path="/runs/:id" element={<RunDetailPage />} />
          <Route path="/tests" element={<TestsPage />} />
          <Route path="/tests/:id" element={<TestDetailPage />} />
          <Route path="/environments" element={<EnvironmentsPage />} />
          <Route path="/test-data" element={<TestDataPage />} />
        </Routes>
      </main>
    </div>
  );
}

export default App;
