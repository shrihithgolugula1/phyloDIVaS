import styles from "./GrantInfoPage.module.css";
import Input from "../../Forms/Input/Input";
import Button from "../../Button/Button";
import Dropdown from "../../Forms/Dropdown/Dropdown";
import { useNavigate } from "react-router-dom";
import React, { useState } from "react";
import { useLocation } from "react-router-dom";
import { API_BASES, apiUrl } from "../../../api/config";
import { getValidIdToken } from "../../../api/auth";

const GrantInfoPage = () => {
    const [formData, setFormData] = useState({
        age: "",
        currentStatus: "",
        highestDegree: "",
        institution: "",
        country: "",
        stateOrProvince: ""
    });

    const [otherStatus, setOtherStatus] = useState("");
    const [message, setMessage] = useState("");
    const navigate = useNavigate();

    const handleChange = (e) => {
        const { name, value } = e.target;

        // If the user changes the country:
        if (name === "country") {
            setFormData((prev) => ({
                ...prev,
                country: value,
                stateOrProvince:
                    value === "United States" || value === "Canada"
                        ? "" // clear for US/Canada so user can pick one
                        : "NA" // auto-fill with "NA" for all other countries
            }));
        } else {
            // Regular update for other fields
            setFormData((prev) => ({
                ...prev,
                [name]: value
            }));
        }
    };

    const handleStatusChange = (e) => {
        handleChange(e);
        if (e.target.value !== "other") setOtherStatus("");
    };

    const handleSubmit = async (e) => {
        e.preventDefault();
        const payload = {
            ...formData,
            currentStatus:
                formData.currentStatus === "other" ? otherStatus : formData.currentStatus
        };
        const idToken = getValidIdToken();
        if (!idToken) {
            setMessage("Your sign-in session is invalid or has expired. Please sign in again.");
            return;
        }
        try {
            const response = await fetch(apiUrl(API_BASES.registration, "/api/Registration/SaveGrantInfo"), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    Authorization: `Bearer ${idToken}`
                },
                body: JSON.stringify(payload)
            });
            if (response.ok) {
                setMessage("Information saved successfully. Redirecting to the home page...");
                setTimeout(() => {
                    navigate("/Home");
                }, 2000);
                setFormData({
                    age: "",
                    currentStatus: "",
                    highestDegree: "",
                    institution: "",
                    country: "",
                    stateOrProvince: ""
                });
                setOtherStatus("");
            } else {
                const errText = await response.text();
                if (response.status === 401) {
                    setMessage("Your sign-in session is invalid or has expired. Please sign in again.");
                } else if (response.status >= 500) {
                    localStorage.setItem("phylodivas.grantInfo", JSON.stringify(payload));
                    setMessage("Information saved on this device. Redirecting to the home page...");
                    setTimeout(() => navigate("/Home"), 2000);
                } else {
                    setMessage("Unable to save your information: " + errText);
                }
            }
        } catch (error) {
            console.error(error);
            setMessage("Error connecting to API.");
        }
    };

    // US States and Canadian Provinces
    const US_STATES = [
        { code: "AL", name: "Alabama" },
        { code: "AK", name: "Alaska" },
        { code: "AZ", name: "Arizona" },
        { code: "AR", name: "Arkansas" },
        { code: "CA", name: "California" },
        { code: "CO", name: "Colorado" },
        { code: "CT", name: "Connecticut" },
        { code: "DE", name: "Delaware" },
        { code: "FL", name: "Florida" },
        { code: "GA", name: "Georgia" },
        { code: "HI", name: "Hawaii" },
        { code: "ID", name: "Idaho" },
        { code: "IL", name: "Illinois" },
        { code: "IN", name: "Indiana" },
        { code: "IA", name: "Iowa" },
        { code: "KS", name: "Kansas" },
        { code: "KY", name: "Kentucky" },
        { code: "LA", name: "Louisiana" },
        { code: "ME", name: "Maine" },
        { code: "MD", name: "Maryland" },
        { code: "MA", name: "Massachusetts" },
        { code: "MI", name: "Michigan" },
        { code: "MN", name: "Minnesota" },
        { code: "MS", name: "Mississippi" },
        { code: "MO", name: "Missouri" },
        { code: "MT", name: "Montana" },
        { code: "NE", name: "Nebraska" },
        { code: "NV", name: "Nevada" },
        { code: "NH", name: "New Hampshire" },
        { code: "NJ", name: "New Jersey" },
        { code: "NM", name: "New Mexico" },
        { code: "NY", name: "New York" },
        { code: "NC", name: "North Carolina" },
        { code: "ND", name: "North Dakota" },
        { code: "OH", name: "Ohio" },
        { code: "OK", name: "Oklahoma" },
        { code: "OR", name: "Oregon" },
        { code: "PA", name: "Pennsylvania" },
        { code: "RI", name: "Rhode Island" },
        { code: "SC", name: "South Carolina" },
        { code: "SD", name: "South Dakota" },
        { code: "TN", name: "Tennessee" },
        { code: "TX", name: "Texas" },
        { code: "UT", name: "Utah" },
        { code: "VT", name: "Vermont" },
        { code: "VA", name: "Virginia" },
        { code: "WA", name: "Washington" },
        { code: "WV", name: "West Virginia" },
        { code: "WI", name: "Wisconsin" },
        { code: "WY", name: "Wyoming" }
    ];

    const CANADA_PROVINCES = [
        { code: "AB", name: "Alberta" },
        { code: "BC", name: "British Columbia" },
        { code: "ON", name: "Ontario" },
        { code: "QC", name: "Quebec" },
        { code: "SK", name: "Saskatchewan" }
    ];

    const OTHER_COUNTRIES = [
        "Australia",
        "Brazil",
        "China",
        "France",
        "Germany",
        "India",
        "Japan",
        "Mexico",
        "United Kingdom",
        "Other"
    ];

    const STATUS = [
        { value: "student", label: "Student" },
        { value: "researcher", label: "Researcher" },
        { value: "other", label: "Other" }
    ];

    const DEGREE = [
        { value: "highschool", label: "High School" },
        { value: "college", label: "College (Bachelor's)" },
        { value: "masters", label: "Master's" },
        { value: "phd", label: "Ph.D." }
    ];

    return (
        <div className={styles.grantInfoContainer}>
            <h2 className={styles.title}>Help Us Improve Our Research</h2>
            <p className={styles.subtitle}>
                We'd love to learn more about our users! Your information helps us better understand
                our community and secure funding for continued development. You can skip this step
                and continue to the site at any time.
            </p>
            {message && <div className={styles.message}>{message}</div>}

            <form className={styles.grantInfoForm} onSubmit={handleSubmit}>

                <div className={styles.formRow}>
                    <label>Age:</label>
                    <Input
                        name="age"
                        placeholder="Enter your age"
                        variant="primary"
                        value={formData.age}
                        onChange={handleChange}
                        required
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Current Status:</label>
                    <Dropdown
                        name="currentStatus"
                        placeholder="Select"
                        variant="primary"
                        options={STATUS}
                        value={formData.currentStatus}
                        onChange={handleStatusChange}
                        required
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Highest Degree:</label>
                    <Dropdown
                        name="highestDegree"
                        placeholder="Select"
                        variant="primary"
                        options={DEGREE}
                        value={formData.highestDegree}
                        onChange={handleChange}
                        required
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Associated Institution:</label>
                    <Input
                        name="institution"
                        placeholder="Enter your institution"
                        variant="primary"
                        value={formData.institution}
                        onChange={handleChange}
                        required
                    />
                </div>

                <div className={styles.formRow}>
                    <label>Country:</label>
                    <Dropdown
                        name="country"
                        placeholder="Select your country"
                        variant="primary"
                        options={[
                            { value: "United States", label: "United States" },
                            { value: "Canada", label: "Canada" },
                            ...OTHER_COUNTRIES.map((c) => ({
                                value: c,
                                label: c
                            }))
                        ]}
                        value={formData.country}
                        onChange={handleChange}
                        required
                    />
                </div>

                {formData.country && (
                    <div className={styles.formRow}>
                        <label>
                            {formData.country === "Canada"
                                ? "Province:"
                                : formData.country === "United States"
                                    ? "State:"
                                    : "State:"}
                        </label>
                        <Dropdown
                            name="stateOrProvince"
                            placeholder="Select"
                            variant="primary"
                            options={
                                formData.country === "United States"
                                    ? US_STATES.map((s) => ({
                                        value: s.code,
                                        label: s.name
                                    }))
                                    : formData.country === "Canada"
                                        ? CANADA_PROVINCES.map((p) => ({
                                            value: p.code,
                                            label: p.name
                                        }))
                                        : [{ value: "NA", label: "N/A" }]
                            }
                            value={formData.stateOrProvince}
                            onChange={handleChange}
                            required={
                                formData.country === "United States" ||
                                formData.country === "Canada"
                            }
                        />
                    </div>
                )}

                <br />

                <div className={styles.altActions}>
                    <div className={styles.altButtonAlign}>
                        <Button
                            label="Cancel"
                            color="grey"
                            variant="secondary"
                            linkTo="/Home"
                        />
                        <Button
                            label="Continue Without Sharing"
                            color="grey"
                            variant="primary"
                            linkTo="/Home"
                        />
                        <Button
                            type="submit"
                            label="Confirm"
                            color="green"
                            variant="primary"
                        />
                    </div>
                </div>
            </form>
        </div>
    );
};

export default GrantInfoPage;
