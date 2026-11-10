import React, { useState, useRef, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import styles from "./FeedbackPage.module.css";
import { API_BASES, apiUrl } from "../../../api/config";

const FeedbackPage = () => {
    const navigate = useNavigate();
    const messageRef = useRef(null);

    const [formData, setFormData] = useState({
        contact_email: "",
        usage_frequency: "",
        overall_experience_rating: "",
        found_what_you_were_looking_for: "",
        navigation_ease: "",
        performance_satisfaction: "",
        recommend_likelihood: "",
        liked_most: "",
        improvements_suggestions: "",
        issues_bugs: "",
        other_comments: ""
    });

    const [message, setMessage] = useState("");

    useEffect(() => {
        window.scrollTo(0, 0);
    }, []);

    useEffect(() => {
        if (messageRef.current) {
            messageRef.current.scrollIntoView({ behavior: "smooth", block: "start" });
        }
    }, [message]);

    const handleChange = (e) => {
        const { name, value, type } = e.target;
        setFormData(prev => ({
            ...prev,
            [name]: type === "radio" && value === "true" ? true :
                    type === "radio" && value === "false" ? false : value
        }));
    };

    const handleSubmit = async (e) => {
        e.preventDefault();

        const setAndScrollMessage = (msg) => {
            setMessage("");
            setTimeout(() => setMessage(msg), 0);
        };

        // Validation
        if (!formData.usage_frequency) {
            setAndScrollMessage("Please select how often you use the app.");
            return;
        }

        if (!formData.overall_experience_rating) {
            setAndScrollMessage("Please rate your overall experience (1–5).");
            return;
        }

        if (formData.found_what_you_were_looking_for === "") {
            setAndScrollMessage("Please answer whether you found what you were looking for.");
            return;
        }

        if (formData.navigation_ease === "") {
            setAndScrollMessage("Please answer whether navigation was easy.");
            return;
        }

        if (!formData.performance_satisfaction) {
            setAndScrollMessage("Please rate your satisfaction with performance (1–5).");
            return;
        }

        if (!formData.recommend_likelihood) {
            setAndScrollMessage("Please rate how likely you are to recommend the app (1–5).");
            return;
        }

        if (formData.contact_email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.contact_email)) {
            setAndScrollMessage("Please enter a valid email address.");
            return;
        }

        // Submit
        try {
            const response = await fetch(apiUrl(API_BASES.feedback, "/api/FeedbackFormTable"), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(formData)
            });

            if (response.ok) {
                setMessage("✅ Thank you for your feedback!");
                setTimeout(() => navigate("/Home"), 2000);
            } else {
                const errorText = await response.text();
                setMessage("❌ Submission failed: " + errorText);
            }
        } catch (error) {
            console.error(error);
            setMessage("⚠️ Error submitting feedback.");
        }
    };

    return (
        <div className={styles.registerContainer}>
            <h2 className={styles.title}>Feedback Form</h2>

            {message && <div ref={messageRef} className={styles.message}>{message}</div>}

            <form className={styles.registerForm} onSubmit={handleSubmit}>
                <div className={styles.formRow}>
                    <label>Email (optional):</label>
                    <input
                        type="email"
                        name="contact_email"
                        placeholder="Enter your email"
                        value={formData.contact_email}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>How often do you use our app?</label>
                    <select
                        name="usage_frequency"
                        value={formData.usage_frequency}
                        onChange={handleChange}
                    >
                        <option value="">Select frequency</option>
                        <option value="Daily">Daily</option>
                        <option value="Weekly">Weekly</option>
                        <option value="Monthly">Monthly</option>
                        <option value="First time">First time</option>
                    </select>
                </div>

                <div className={styles.formRow}>
                    <label>Overall experience rating (1–5):</label>
                    <select
                        name="overall_experience_rating"
                        value={formData.overall_experience_rating}
                        onChange={handleChange}
                    >
                        <option value="">Select rating</option>
                        {[1,2,3,4,5].map(num => (
                            <option key={num} value={num}>{num}</option>
                        ))}
                    </select>
                </div>

                <div className={styles.formRow}>
                    <label>Found what you were looking for?</label>
                    <div>
                        <label>
                            <input type="radio" name="found_what_you_were_looking_for" value="true" onChange={handleChange} /> Yes
                        </label>
                        <label>
                            <input type="radio" name="found_what_you_were_looking_for" value="false" onChange={handleChange} /> No
                        </label>
                    </div>
                </div>

                <div className={styles.formRow}>
                    <label>Was navigation easy?</label>
                    <div>
                        <label>
                            <input type="radio" name="navigation_ease" value="true" onChange={handleChange} /> Yes
                        </label>
                        <label>
                            <input type="radio" name="navigation_ease" value="false" onChange={handleChange} /> No
                        </label>
                    </div>
                </div>

                <div className={styles.formRow}>
                    <label>Performance satisfaction (1–5):</label>
                    <select
                        name="performance_satisfaction"
                        value={formData.performance_satisfaction}
                        onChange={handleChange}
                    >
                        <option value="">Select rating</option>
                        {[1,2,3,4,5].map(num => (
                            <option key={num} value={num}>{num}</option>
                        ))}
                    </select>
                </div>

                <div className={styles.formRow}>
                    <label>Likelihood to recommend (1–5):</label>
                    <select
                        name="recommend_likelihood"
                        value={formData.recommend_likelihood}
                        onChange={handleChange}
                    >
                        <option value="">Select rating</option>
                        {[1,2,3,4,5].map(num => (
                            <option key={num} value={num}>{num}</option>
                        ))}
                    </select>
                </div>

                <div className={styles.formRow}>
                    <label>What did you like most?</label>
                    <textarea
                        name="liked_most"
                        placeholder="Your favorite feature or aspect..."
                        value={formData.liked_most}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Suggestions for improvement:</label>
                    <textarea
                        name="improvements_suggestions"
                        placeholder="What could be improved?"
                        value={formData.improvements_suggestions}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Issues or bugs encountered:</label>
                    <textarea
                        name="issues_bugs"
                        placeholder="Describe any problems..."
                        value={formData.issues_bugs}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Other comments:</label>
                    <textarea
                        name="other_comments"
                        placeholder="Any additional feedback?"
                        value={formData.other_comments}
                        onChange={handleChange}
                    />
                </div>

                <div className={styles.registerButton}>
                    <button type="submit" className={styles.buttonLink}>Submit Feedback</button>
                </div>
            </form>
        </div>
    );
};

export default FeedbackPage;
