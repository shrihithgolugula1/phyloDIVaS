import React, { useState } from 'react';
import styles from './AccountSettings.module.css';
import Button from '../Button/Button';
import { useEffect } from 'react';
import { API_BASES, apiUrl } from '../../api/config';

const AccountSettings = () => {

    useEffect(() => {
        const fetchGrantInfo = async () => {
            const idToken = sessionStorage.getItem('idToken');
            if (!idToken) return;

            try {
                const response = await fetch(apiUrl(API_BASES.account, "/GetGrantInfo"), {
                    method: 'GET',
                    headers: {
                        'Authorization': `Bearer ${idToken}`,
                        'Content-Type': 'application/json'
                    }
                });

                if (!response.ok) {
                    console.error('Failed to fetch grant info. Status:', response.status);
                    return;
                }

                const data = await response.json();

                setFormData((prev) => ({
                    ...prev,
                    ...data
                }));

            } catch (error) {
                console.error('Error fetching grant info:', error);
            }
        };

        fetchGrantInfo();
    }, []);

  const STATUS = [
    { value: "Student", label: "Student" },
    { value: "Researcher", label: "Researcher" },
    { value: "Other", label: "Other" }
];

const DEGREE = [
    { value: "Highschool", label: "High School" },
    { value: "College", label: "College (Bachelor's)" },
    { value: "Masters", label: "Master's" },
    { value: "Phd", label: "Ph.D." }
];

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
  
  //const GENDER = ["Male", "Female", "Other", "Prefer not to say"];

    const [formData, setFormData] = useState({
        username: "",
        email: "",
        shareForGrants: true,
        age: "",
        currentStatus: "",
        highestDegree: "",
        associatedInstitution: "",
        country: "",
        stateOrProvince: "",
        guidedTours: false
    });
    const [otherStatus, setOtherStatus] = useState("");

    

  const [isEditing, setIsEditing] = useState(false);

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target;

    // For checkboxes
    if (type === "checkbox") {
        setFormData(prev => ({ ...prev, [name]: checked }));
        return;
    }

    // Special handling for country
    if (name === "country") {
        setFormData(prev => ({
            ...prev,
            country: value,
            stateOrProvince:
                value === "United States" || value === "Canada" ? "" : "NA"
        }));
        return;
    }

    setFormData(prev => ({ ...prev, [name]: value }));

    // Clear otherStatus if currentStatus changes
    if (name === "currentStatus" && value !== "other") setOtherStatus("");
  };

  const handleSave = async () =>  {
    const idToken = sessionStorage.getItem("idToken");
    if (!idToken) {
        console.error("Missing ID token");
        return;
    }

    const payload = {
        age: formData.age,
        currentStatus: formData.currentStatus,
        highestDegree: formData.highestDegree,
        institution: formData.associatedInstitution,
        country: formData.country,
        stateOrProvince: formData.state
    };

    try {
        const response = await fetch(apiUrl(API_BASES.registration, "/api/registration/SaveGrantInfo"), {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Authorization": `Bearer ${idToken}`
            },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            console.error("Save failed", await response.text());
            return;
        }

        console.log("Grant info saved successfully!");
        setIsEditing(false);

    } catch (err) {
        console.error("Error saving grant info:", err);
    }
  };

  const handleCancel = () => {
    setIsEditing(false);
  };

  return (
    <div className={styles.body}>
      <div className={styles.accountSettingsSection}>
        <div className={styles.accountSettingsContainer}>

        
      <div className={styles.formContainer}>
        {/* Username */}
        <div className={styles.formGroup}>
          <label className={styles.formLabel}>Username</label>
          {isEditing ? (
            <input
              type="text"
              name="username"
              value={formData.username}
              onChange={handleChange}
              className={styles.formInput}
            />
          ) : (
            <div className={styles.displayValue}>{formData.username}</div>
          )}
        </div>

        {/* Email */}
        <div className={styles.formGroup}>
          <label className={styles.formLabel}>Email</label>
          {isEditing ? (
            <input
              type="email"
              name="email"
              value={formData.email}
              onChange={handleChange}
              className={styles.formInput}
            />
          ) : (
            <div className={styles.displayValue}>{formData.email}</div>
          )}
        </div>



        {/* Grant Information Details */}
        
            <h3 className={styles.subsectionTitle}>Grant Information</h3>
            
                    {/* Grant Information Sharing */}
            <div className={styles.formGroup}>
            <div className={styles.toggleGroup}>
                <div className={styles.toggleInfo}>
                <label className={styles.formLabel}>Share Information for Grants</label>
                <p className={styles.helperText}>
                    Allow us to share your information with potential grant opportunities
                </p>
                </div>
                <button
                onClick={() => handleChange({ target: { name: 'shareForGrants', type: 'checkbox', checked: !formData.shareForGrants } })}
                className={`${styles.toggle} ${formData.shareForGrants ? styles.toggleActive : ''}`}
                disabled={!isEditing}
                >
                <span className={styles.toggleSlider} />
                </button>
            </div>
            </div>

            {formData.shareForGrants && (
            <div className={styles.grantInfoSection}>
                <div className={styles.formRow}>
                <div className={styles.formGroup}>
                    <label className={styles.formLabel}>Age</label>
                    {isEditing ? (
                    <input
                        type="text"
                        name="age"
                        value={formData.age}
                        onChange={handleChange}
                        placeholder="Enter your age"
                        className={styles.formInput}
                    />
                    ) : (
                    <div className={styles.displayValue}>{formData.age || 'Not provided'}</div>
                    )}
                </div>

                <div className={styles.formGroup}>
                    <label className={styles.formLabel}>Current Status</label>
                    {isEditing ? (
                    <select
                        name="currentStatus"
                        value={formData.currentStatus}
                        onChange={handleChange}
                >
                        <option value="">Select your Current Status</option>
                        {STATUS.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
                    </select>
                    ) : (
                    <div className={styles.displayValue}>{formData.currentStatus || 'Not provided'}</div>
                    )}
                    {formData.currentStatus === "other" && isEditing && (
                      <input
                          type="text"
                          name="otherStatus"
                          value={otherStatus}
                          onChange={handleChange}
                          placeholder="Please specify"
                      />
                  )}
                </div>
                </div>

                <div className={styles.formRow}>
                <div className={styles.formGroup}>
                    <label className={styles.formLabel}>Highest Degree</label>
                    {isEditing ? (
                    <select
                        name="highestDegree"
                        value={formData.highestDegree}
                        onChange={handleChange}
                    >
                        <option value="">Select your Degree</option>
                        {DEGREE.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
                    </select>
                    ) : (
                    <div className={styles.displayValue}>{formData.highestDegree || 'Not provided'}</div>
                    )}
                </div>

                <div className={styles.formGroup}>
                    <label className={styles.formLabel}>Associated Institution</label>
                    {isEditing ? (
                    <input
                        type="text"
                        name="associatedInstitution"
                        value={formData.associatedInstitution}
                        onChange={handleChange}
                        placeholder="Enter your institution"
                        className={styles.formInput}
                    />
                    ) : (
                    <div className={styles.displayValue}>{formData.associatedInstitution || 'Not provided'}</div>
                    )}
                </div>
                </div>

                <div className={styles.formRow}>
                <div className={styles.formGroup}>
                    <label className={styles.formLabel}>Country</label>
                    {isEditing ? (
                    <select
                        name="country"
                        value={formData.country}
                        onChange={handleChange}
                    >
                        <option value="">Select your Country</option>
                        <option value="United States">United States</option>
                        <option value="Canada">Canada</option>
                        {OTHER_COUNTRIES.map(c => <option key={c} value={c}>{c}</option>)}
                    </select>
                    ) : (
                    <div className={styles.displayValue}>{formData.country || 'Not provided'}</div>
                    )}
                </div>

                <div className={styles.formGroup}>
                    <label className={styles.formLabel}>State/Province</label>
                    {isEditing ? (
                    <select
                        name="stateOrProvince"
                        value={formData.stateOrProvince}
                        onChange={handleChange}
                        disabled={formData.country !== "United States" && formData.country !== "Canada"}
                    >
                        {formData.country === "United States" &&
                            US_STATES.map(s => <option key={s.code} value={s.code}>{s.name}</option>)
                        }
                        {formData.country === "Canada" &&
                            CANADA_PROVINCES.map(p => <option key={p.code} value={p.code}>{p.name}</option>)
                        }
                        {formData.country !== "United States" && formData.country !== "Canada" &&
                            <option value="NA">N/A</option>
                        }
                    </select>
                    ) : (
                    <div className={styles.displayValue}>{formData.state || 'Not provided'}</div>
                    )}
                </div>
              </div>
            </div>
        )}

        <br />

        {/* Guided Tours */}
        <div className={styles.formGroup}>
          <div className={styles.toggleGroup}>
            <div className={styles.toggleInfo}>
              <label className={styles.formLabel}>Guided Tours</label>
              <p className={styles.helperText}>
                Enable interactive tours to help you navigate the website
              </p>
            </div>
            <button
              onClick={() => handleChange({ target: { name: 'guidedTours', type: 'checkbox', checked: !formData.guidedTours } })}
              className={`${styles.toggle} ${formData.guidedTours ? styles.toggleActive : ''}`}
            >
              <span className={styles.toggleSlider} />
            </button>
          </div>
        </div>

        <br />

        {/* Action Buttons */}
        <div className={styles.formActions}>
          {isEditing ? (
            <>
              <Button onClick={handleSave} label="Save Changes" color="blue" />
              <Button onClick={handleCancel} label="Cancel" color="grey" />
            </>
          ) : (
            <Button onClick={() => setIsEditing(true)} label="Edit Settings" color="blue" />
          )}
        </div>
      </div>
    </div>

  
        </div>
      </div>
  );
};

export default AccountSettings;
