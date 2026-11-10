import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useLocation } from "react-router-dom";
import styles from "./RegistrationVerification.module.css";
import { useEffect } from 'react';
import { API_BASES, apiUrl } from "../../api/config";


const RegistrationVerification = () => {

    // initialize the username, email, and verification code
    const [username, setUsername] = useState("");
    const [email, setEmail] = useState("");
    const [code, setCode] = useState("");
    const [message, setMessage] = useState("");
    const navigate = useNavigate();    // allows for routing

    // returns an object containing various details about the current URL
    const location = useLocation();
    // load the username and email from query params on first render
    useEffect(() => {
        const params = new URLSearchParams(location.search);
        const paramUsername = params.get("username");
        const paramEmail = params.get("email");

        if (paramUsername) {
            setUsername(paramUsername);
        }
        if (paramEmail) {
            setEmail(paramEmail)
        }
    }, [location]);


    // function to handle submisson of username and verification code
    const handleSubmit = async (e) => {
        e.preventDefault();

        try {
            const response = await fetch(apiUrl(API_BASES.registration, "/api/Registration/verify"), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ username, code })
            });

            const result = await response.text();

            if (response.ok) {
                setMessage("Verification successful! Redirecting to login...");
                setTimeout(() => {
                    navigate("/Login"); // redirect to Login upon successful verification; user needs to Login to be authenticated
                }, 2000);
            } else {
                setMessage("Verification failed: " + result);
            }
        } catch (error) {
            console.error(error);
            setMessage("Error connecting to API.");
        }
    };

    return (
        <div className={styles.verificationContainer}>
            <h2 className={styles.title}>Verify New Account</h2>
            {message && <div className={styles.message}>{message}</div>}
            <p>Please enter the code that was sent to your email.</p>

            <form className={styles.verificationForm} onSubmit={handleSubmit}>
                <div className={styles.formRow}>
                    <label>Username:</label>
                    <input
                        type="text"
                        value={username}
                        disabled
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Email:</label>
                    <input
                        type="text"
                        value={email}
                        disabled
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Verification Code:</label>
                    <input
                        type="text"
                        value={code}
                        onChange={(e) => setCode(e.target.value)}
                        required
                    />
                </div>

                <button type="submit" className={styles.verifyButton}>Verify</button>
            </form>
        </div>
    );
};

export default RegistrationVerification;
