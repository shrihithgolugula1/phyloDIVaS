import React, { useState, useEffect } from 'react';
import styles from './AdminDashboard.module.css';
import Button from '../Button/Button';
import { API_BASES, apiUrl } from '../../api/config';

const ProfileTables = () => {
    const [activeTab, setActiveTab] = useState("users");
    const [users, setUsers] = useState([]);
    const [errors, setErrors] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [refreshTrigger, setRefreshTrigger] = useState(0);

    const handleDelete = async (id) => {
    try {
        const token = sessionStorage.getItem("accessToken");
        const response = await fetch(apiUrl(API_BASES.account, "/api/Admin/RemoveUser?userId=" + encodeURIComponent(id)), {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",

                    },
                    body: JSON.stringify(token)
                });
        if (response.ok) {
                // Trigger a refresh by updating the state
                setRefreshTrigger(prev => prev + 1);
            } else {
                console.error("Delete failed:", response.status);
            }

    } catch (err) {
      console.error("Error deleting search:", err);
    }
  };
    


    useEffect(() => {

        const token = sessionStorage.getItem("accessToken");

        if (!token) {
            setError("Not authenticated.");
            setLoading(false);
            return;
        }

        const fetchUsers = async () => {
            try {
                setLoading(true);
                setError(null);

                const response = await fetch(apiUrl(API_BASES.account, "/api/Admin/GetAllUsers"), {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                    },
                    body: JSON.stringify(token)
                });

                if (!response.ok) {
                    if (response.status === 401)
                        throw new Error("Unauthorized. Please log in again.");
                    if (response.status === 403)
                        throw new Error("Forbidden. Admin access required.");
                    throw new Error(`HTTP ${response.status}`);       
                }

                const data = await response.json();
                setUsers(data);
            } catch (err) {
                console.error(err);
                setError("Failed to load users.");
            } finally {
                setLoading(false);
            }
        };

        const fetchErrors = async () => {
            try {
                setLoading(true);
                setError(null);

                const response = await fetch(apiUrl(API_BASES.account, "/api/Admin/GetErrorLogs"), {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                       "Authorization": `Bearer ${token}`
                    },
                    body: JSON.stringify(token)
                });

                if (!response.ok) {
                    if (response.status === 401)
                        throw new Error("Unauthorized. Please log in again.");
                    if (response.status === 403)
                        throw new Error("Forbidden. Admin access required.");
                    throw new Error(`HTTP ${response.status}`);
                }

                const data = await response.json();
                setErrors(data);
            } catch (err) {
                console.error(err);
                setError("Failed to load error logs.");
            } finally {
                setLoading(false);
            }
        };

        if (activeTab === "users") {
            fetchUsers();
        }
        if (activeTab === "errors") {
            fetchErrors();
        }
    }, [activeTab,refreshTrigger]);



    return (
        <div className={styles.body}>
            <div className={styles.savedSearchSection}>
                <div className={styles.savedSearchContainer}>

                    {/* ===== Toggle Buttons ===== */}
                    <div style={{ display: "flex", gap: "10px", marginBottom: "20px" }}>
                        <button
                            onClick={() => setActiveTab("users")}
                            className={activeTab === "users" ? styles.adminActiveTab : styles.adminInactiveTab}
                        >
                            Users
                        </button>

                        <button
                            onClick={() => setActiveTab("errors")}
                            className={activeTab === "errors" ? styles.adminActiveTab : styles.adminInactiveTab}
                        >
                            API Error Logs
                        </button>
                    </div>

                    {/* ===== USERS TABLE ===== */}
                    {activeTab === "users" && (
                        <>
                            <div className={styles.sectionHeader}>
                                <h2 className={styles.sectionTitle}>Users</h2>
                                <p className={styles.sectionSubtitle}>List of registered users</p>
                            </div>

                            {users.length === 0 ? (
                                <div className={styles.emptyState}>
                                    <p>No users found</p>
                                </div>
                            ) : (
                                <table className={styles.savedSearchesTable}>
                                    <thead>
                                        <tr>
                                            <th className={styles.headerCell}>User ID</th>
                                            <th className={styles.headerCell}>Age</th>
                                            <th className={styles.headerCell}>Current Status</th>
                                            <th className={styles.headerCell}>Highest Degree</th>
                                            <th className={styles.headerCell}>Institution</th>
                                            <th className={styles.headerCell}>State</th>
                                            <th className={styles.headerCell}>Country</th>
                                            <th className={styles.headerCell}>Updated</th>
                                            <th className={styles.headerCell}>Delete</th>
                                        </tr>
                                    </thead>

                                    <tbody>
                                        {users.map((user) => (
                                            <tr key={user.userId}>
                                                <td className={styles.tableCell}>{user.userId}</td>
                                                <td className={styles.tableCell}>{user.age ?? "—"}</td>
                                                <td className={styles.tableCell}>{user.currentStatus || "—"}</td>
                                                <td className={styles.tableCell}>{user.highestDegree || "—"}</td>
                                                <td className={styles.tableCell}>{user.associatedInstitution || "—"}</td>
                                                <td className={styles.tableCell}>{user.state || "—"}</td>
                                                <td className={styles.tableCell}>{user.country || "—"}</td>
                                                <td className={styles.tableCell}>
                                                    {user.updatedAt
                                                        ? new Date(user.updatedAt).toLocaleDateString()
                                                        : "—"}
                                                </td>
                                                <td className={styles.tableCell}>
                                                <Button onClick={() => handleDelete(user.userId)} label={"Delete"} color={"pink"}/>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            )}
                        </>
                    )}

                    {/* ===== ERROR LOGS TABLE ===== */}
                    {activeTab === "errors" && (
                        <>
                            <div className={styles.sectionHeader}>
                                <h2 className={styles.sectionTitle}>API Error Logs</h2>
                                <p className={styles.sectionSubtitle}>View backend error activity</p>
                            </div>

                            <table className={styles.savedSearchesTable}>
                                <thead>
                                    <tr>
                                        <th className={styles.headerCell}>Route</th>
                                        <th className={styles.headerCell}>Message</th>
                                        <th className={styles.headerCell}>Time</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {errors.map((e) => (
                                        <tr key={e.id}>
                                            <td className={styles.tableCell}>{e.route}</td>
                                            <td className={styles.tableCell}>{e.message}</td>
                                            <td className={styles.tableCell}>{e.time}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </>
                    )}

                </div>
            </div>
        </div>
    );
};

export default ProfileTables;
