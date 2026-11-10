import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import styles from "./RegisterPage.module.css";
import { API_BASES, apiUrl } from "../../api/config";

const RegisterPage = () => {
    const [formData, setFormData] = useState({
        username: "",
        email: "",
        password: ""
    });

    const [message, setMessage] = useState("");
    const navigate = useNavigate();

    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>/?]).{8,}$/;

    const handleChange = (e) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));
    };

    const handleSubmit = async (e) => {
        e.preventDefault();

        if (!passwordRegex.test(formData.password)) {
            setMessage("Password must include uppercase, lowercase, number, and special character.");
            return;
        }

        try {
            const response = await fetch(apiUrl(API_BASES.registration, "/api/Registration"), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(formData)
            });

            const result = await response.text();
            if (response.ok) {
                setMessage("Registration successful! Redirecting to verification...");
                setTimeout(() => {
                    // Redirect to verification and pass username + email in query params
                    navigate(`/RegistrationVerification?username=${encodeURIComponent(formData.username)}&email=${encodeURIComponent(formData.email)}`);
                }, 2000);
            } else {
                setMessage("Registration failed: " + result);
            }
        } catch (error) {
            console.error(error);
            setMessage("Error connecting to API.");
        }
    };

    return (
        <div className={styles.registerContainer}>
            <h2 className={styles.title}>Register</h2>
            {message && <div className={styles.message}>{message}</div>}
            <form className={styles.registerForm} onSubmit={handleSubmit}>
                <div className={styles.formRow}>
                    <label>Username:</label>
                    <input
                        type="text"
                        name="username"
                        placeholder="Enter your username"
                        value={formData.username}
                        onChange={handleChange}
                        required
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Email:</label>
                    <input
                        type="email"
                        name="email"
                        placeholder="Enter your email"
                        value={formData.email}
                        onChange={handleChange}
                        required
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Password:</label>
                    <input
                        type="password"
                        name="password"
                        placeholder="Enter your password"
                        value={formData.password}
                        onChange={handleChange}
                        required
                    />
                </div>

                <button type="submit" className={styles.registerButton}>Register</button>
            </form>
        </div>
    );
};

export default RegisterPage;
