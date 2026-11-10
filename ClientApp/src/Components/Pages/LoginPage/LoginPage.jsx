import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import styles from "./LoginPage.module.css";
import Button from "../../Button/Button";
import Input from "../../Forms/Input/Input";
import { login, me } from "../../../api/auth";

const LoginPage = () => {
    const [username, setUsername] = useState("");
    const [password, setPassword] = useState("");
    const [error, setError] = useState("");
    const [loading, setLoading] = useState(false);
    const navigate = useNavigate();

    async function handleSubmit(e) {
        e.preventDefault();
        setError("");
        setLoading(true);

        try {
            const auth = await login(username, password);

            // store tokens (sessionStorage for now)
            sessionStorage.setItem("accessToken", auth.accessToken);
            sessionStorage.setItem("idToken", auth.idToken);
            sessionStorage.setItem("refreshToken", auth.refreshToken);

            // optional: fetch profile to confirm
            await me(auth.accessToken);
            
            navigate("/Home");
            
        } catch (err) {
            setError(err?.message || "Unable to log in. Please try again.");
        } finally {
            setLoading(false);
        }
    }

    return (
        <div className={styles.loginContainer}>
            <h2 className={styles.title}>Welcome Back!</h2>

            <form className={styles.loginForm} onSubmit={handleSubmit}>
                <div className={styles.formRow}>
                    <label>Username:</label>
                    <Input
                        placeholder="Enter your username"
                        variant="primary"
                        name="username"
                        value={username}
                        onChange={(e) => setUsername(e.target.value)}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Password:</label>
                    <Input
                        placeholder="Enter your password"
                        variant="primary"
                        name="password"
                        type="password"
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                    />
                </div>

                {error && <div style={{ color: "crimson", marginBottom: 12 }}>{error}</div>}

                <Button
                    label={loading ? "Logging in..." : "Login"}
                    color="green"
                    variant="primary"
                    type="submit"
                    fullWidth
                    disabled={loading}
                />

                <p className={styles.separation}>—— OR ——</p>

                <Button label="Register" color="grey" variant="secondary" linkTo="/Register" fullWidth />
                <br />
                <Button label="Continue Without Logging In" color="grey" variant="secondary" linkTo="/Search" fullWidth />
            </form>
        </div>
    );
};

export default LoginPage;
