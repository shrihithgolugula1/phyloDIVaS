import { useEffect } from "react";
import Navbar from "./Components/ToDo/Navbar/Navbar";
import LoginPage from "./Components/Pages/LoginPage/LoginPage";
import RegisterPage from "./Components/Pages/RegisterPage/RegisterPage";
import GrantInfoPage from "./Components/Pages/GrantInfoPage/GrantInfoPage";
import HomePage from "./Components/Pages/HomePage/HomePage";
import GeneSearchPage from "./Components/Pages/GeneSearchPage/GeneSearchPage";
import RegistrationVerification from "./Components/Pages/RegistrationVerification/RegistrationVerification";
import GenomeSearchPage from "./Components/Pages/GenomeSearchPage/GenomeSearchPage";
import GenomeAnalysisPage from "./Components/GenomeAnalysisPage/GenomeAnalysisPage";
import { Route, Routes, useLocation } from 'react-router-dom';
import './App.css';
import Footer from "./Components/ToDo/Footer/Footer";
import HelpPage from "./Components/Pages/HelpPage/HelpPage";
import DisclaimerPage from "./Components/Pages/DisclaimerPage/DisclaimerPage";
import FeedbackPage from "./Components/Pages/FeedbackPage/FeedbackPage";
import SavedSearches from "./Components/SavedSearches/SavedSearches";
import ProfilePage from "./Components/Pages/ProfilePage/ProfilePage";
import AboutPage from "./Components/Pages/AboutPage/AboutPage";
import CitationsPage from "./Components/Pages/CitationsPage/CitationsPage";
import "./Components/Accessibility/accessibility.css";
import "./Components/Accessibility/accessibility.js";

const PAGE_TITLES = {
    "/": "phyloDIVaS | Home",
    "/home": "phyloDIVaS | Home",
    "/search": "phyloDIVaS | Genome Search",
    "/analysis": "phyloDIVaS | Genome Analysis",
    "/login": "phyloDIVaS | Sign In",
    "/register": "phyloDIVaS | Create Account",
    "/grantinfo": "phyloDIVaS | Grant Information",
    "/genesearch": "phyloDIVaS | Gene Search",
    "/registrationverification": "phyloDIVaS | Verify Your Account",
    "/profile": "phyloDIVaS | Profile & Saved Searches",
    "/help": "phyloDIVaS | Help Center",
    "/feedback": "phyloDIVaS | Feedback",
    "/disclaimer": "phyloDIVaS | Disclaimer",
    "/about": "phyloDIVaS | About",
    "/citations": "phyloDIVaS | Research Tool Citations",
};

const DEFAULT_TITLE = "phyloDIVaS";

function usePageTitleUpdater() {
    const location = useLocation();

    useEffect(() => {
        const normalizedPath = location.pathname.toLowerCase();
        document.title = PAGE_TITLES[normalizedPath] || DEFAULT_TITLE;
    }, [location.pathname]);
}


function App() {
    usePageTitleUpdater();

    return (
        <div className="body">
            <Navbar />

            <div>
                <Routes>
                    <Route path="/" element={<HomePage />} />
                    <Route path="/Search" element={<GenomeSearchPage />} />
                    <Route path="/Analysis" element={<GenomeAnalysisPage />} />
                    <Route path="/Login" element={<LoginPage />} />
                    <Route path="/Home" element={<HomePage />} />
                    <Route path="/Register" element={<RegisterPage />} />
                    <Route path="/GrantInfo" element={<GrantInfoPage />} />
                    <Route path="/GeneSearch" element={<GeneSearchPage />} />
                    <Route path="/RegistrationVerification" element={<RegistrationVerification />} />
                    <Route path="/Profile" element={<ProfilePage />} />
                    <Route path="/About" element={<AboutPage />} />
                    <Route path="/Citations" element={<CitationsPage />} />

                    <Route path="/Help" element={<HelpPage />} />
                    <Route path="/Feedback" element={<FeedbackPage />} />
                    <Route path="/Disclaimer" element={<DisclaimerPage />} />
                </Routes>
            </div>


            {/* Accessibility Panel Trigger */}
            <button id="accessibilityToggle" className="accessibility-btn" aria-label="Accessibility settings" aria-expanded="false" aria-controls="accessibilityPanel">
                ♿
            </button>

            {/* Accessibility Panel */}
            <div id="accessibilityPanel" className="accessibility-panel" role="dialog" aria-label="Accessibility settings" hidden>
                <h4 className="panel-title">Accessibility Settings</h4>

                <div className="setting-group">
                    <label className="setting-label">Font Size</label>
                    <div className="setting-controls">
                        <button className="acc-btn" onClick={() => window.decreaseFont()}>A-</button>
                        <button className="acc-btn" onClick={() => window.increaseFont()}>A+</button>
                    </div>
                </div>

                <div className="setting-group">
                    <label className="setting-label">Color Modes</label>
                    <div className="setting-controls">
                        <button id="contrastModeBtn" className="acc-btn" aria-pressed="false" onClick={() => window.toggleContrast()}>
                            Enable High Contrast
                        </button>
                        <button
                            id="darkModeBtn"
                            className="acc-btn"
                            aria-pressed="false"
                            onClick={() => window.toggleDarkMode()}
                        >
                            Dark Mode
                        </button>
                    </div>
                </div>
                <div className="setting-group">
                    <label className="setting-label">Color-Blind Presets</label>
                    <select id="colorBlindSelect" className="form-select acc-select"
                        onChange={() => window.applyColorBlindMode()}>
                        <option value="none">None</option>
                        <option value="protanopia">Protanopia</option>
                        <option value="deuteranopia">Deuteranopia</option>
                        <option value="tritanopia">Tritanopia</option>
                    </select>
                </div>

                <div className="setting-group">
                    <label className="setting-label">Motion</label>
                    <div className="setting-controls">
                        <button id="motionModeBtn" className="acc-btn" aria-pressed="false" onClick={() => window.toggleReducedMotion()}>Reduce Motion</button>
                    </div>
                </div>

                <div className="setting-group">
                    <label className="setting-label">Alt-Text Mode</label>
                    <div className="setting-controls">
                        <button id="altTextModeBtn" className="acc-btn" aria-pressed="false" onClick={() => window.toggleAltTextMode()}>
                            Show Text Alternatives
                        </button>
                    </div>
                </div>

                <button className="acc-btn reset-accessibility" onClick={() => window.resetAccessibility()}>
                    Reset Accessibility Settings
                </button>
            </div>

            <Footer />
        </div>
    );
}

export default App;
