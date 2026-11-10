import styles from "./RegisterPage.module.css";
import Button from "../../Button/Button";
import Input from "../../Forms/Input/Input";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { API_BASES, apiUrl } from "../../../api/config";

const RegisterPage = () => {
    const [formData, setFormData] = useState({
        username: "",
        email: "",
        password: "",
        confirmPassword: ""
    });

    const [errors, setErrors] = useState({
        missingFields: "",
        passwordMismatch: "",
        passwordInvalid: "",
        apiError: ""
    });

    const [successMessage, setSuccessMessage] = useState("");

    const navigate = useNavigate();

    // ✅ Password requirements: 1 upper, 1 lower, 1 number, 1 special char
    const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*]).{8,}$/;

    // ✅ Live validation
    const handleChange = (e) => {
        const { name, value } = e.target;
        const updatedForm = { ...formData, [name]: value };
        setFormData(updatedForm);

        // Reset "missing fields" message when typing
        setErrors(prev => ({ ...prev, missingFields: "" }));

        // Live password validation
        if (name === "password" || name === "confirmPassword") {
            const liveErrors = { ...errors };

            if (
                updatedForm.password &&
                updatedForm.confirmPassword &&
                updatedForm.password !== updatedForm.confirmPassword
            ) {
                liveErrors.passwordMismatch = "Passwords do not match.";
            }
            else {
                liveErrors.passwordMismatch = "";
            }

            //if (updatedForm.password && !passwordRegex.test(updatedForm.password)) {
            //    liveErrors.passwordInvalid =
            //        "Password must include at least 1 uppercase, 1 lowercase, 1 number, and 1 special character.";
            //}
            //else {
            //    liveErrors.passwordInvalid = "";
            //}

            setErrors(liveErrors);
        }
    };

    // ✅ On submit
    const handleSubmit = async (e) => {
        e.preventDefault();
        setErrors({
            missingFields: "",
            passwordMismatch: "",
            passwordInvalid: "",
            apiError: ""
        });
        setSuccessMessage("");

        const { username, email, password, confirmPassword } = formData;

        // Check for missing fields
        if (!username || !email || !password || !confirmPassword) {
            setErrors(prev => ({
                ...prev,
                missingFields: "Please fill out all fields before submitting."
            }));
            return;
        }

        // Password checks
        let hasError = false;
        let newErrors = { passwordMismatch: "", passwordInvalid: "", apiError: "", missingFields: "" };

        if (password !== confirmPassword) {
            newErrors.passwordMismatch = "Passwords do not match.";
            hasError = true;
        }

        if (!passwordRegex.test(password)) {
            newErrors.passwordInvalid =
                "Password must include at least one uppercase letter, one lowercase letter, one number, and one special character.";
            hasError = true;
        }

        if (hasError) {
            setErrors(newErrors);
            return;
        }

        // ✅ API call
        try {
            const response = await fetch(apiUrl(API_BASES.registration, "/api/Registration"), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ username, email, password })
            });

            if (response.ok) {
                await response.json().catch(() => null);
                setSuccessMessage("Registration successful! Redirecting to verification...");
                setFormData({ username: "", email: "", password: "", confirmPassword: "" });
                setTimeout(() => {
                    navigate(`/RegistrationVerification?username=${encodeURIComponent(formData.username)}&email=${encodeURIComponent(formData.email)}`); 
                }, 2000);
            } else {
                const payload = await response.json().catch(() => null);
                setErrors({ ...newErrors, apiError: payload?.error || "Registration could not be completed." });
            }
        } catch (error) {
            setErrors({ ...newErrors, apiError: "Error connecting to server." });
        }
    };

    return (
        <div className={styles.registerContainer}>
            <h2 className={styles.title}>Register</h2>

            {/* ✅ Error + success messages */}
            {errors.missingFields && <p className={styles.error}>{errors.missingFields}</p>}
            {errors.passwordMismatch && <p className={styles.error}>{errors.passwordMismatch}</p>}
            {errors.passwordInvalid && <p className={styles.error}>{errors.passwordInvalid}</p>}
            {errors.apiError && <p className={styles.error}>{errors.apiError}</p>}
            {successMessage && <p className={styles.success}>{successMessage}</p>}

            <form className={styles.registerForm} onSubmit={handleSubmit}>
                <div className={styles.formRow}>
                    <label>Username:</label>
                    <Input
                        placeholder="Enter a username"
                        variant="primary"
                        name="username"
                        value={formData.username}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Email:</label>
                    <Input
                        placeholder="Enter your email"
                        variant="primary"
                        name="email"
                        value={formData.email}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Password:</label>
                    <Input
                        placeholder="Enter a password"
                        type="password"
                        variant="primary"
                        name="password"
                        value={formData.password}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Confirm Password:</label>
                    <Input
                        placeholder="Confirm your password"
                        type="password"
                        variant="primary"
                        name="confirmPassword"
                        value={formData.confirmPassword}
                        onChange={handleChange}
                    />
                </div>

                <br />
                <div className={styles.registerButton}>
                    <Button label="Register" color="green" variant="primary" type="submit" />
                </div>
            </form>
        </div>
    );
};

export default RegisterPage;
