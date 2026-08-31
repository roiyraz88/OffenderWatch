import { NavLink, Route, Routes } from "react-router-dom";
import { useHealth } from "./hooks/useHealth";
import { DashboardPage } from "./pages/DashboardPage";
import { RunsPage } from "./pages/RunsPage";
import { RunDetailPage } from "./pages/RunDetailPage";
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

function App() {
  const health = useHealth();

  return (
    <div>
      <header>
        <strong>OffenderWatch — Test Management</strong>
        <nav>
          {navItems.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end}>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <span title="API connectivity">API: {health}</span>
      </header>

      <main>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/runs" element={<RunsPage />} />
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
