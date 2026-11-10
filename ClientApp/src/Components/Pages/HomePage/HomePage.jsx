import { useState, useEffect } from "react";
import styles from "./HomePage.module.css";
import CardRow from "../../Card/CardRow/CardRow";
import Card from "../../Card/Card";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faBookmark, faUsers, faSearch, faChartLine } from "@fortawesome/free-solid-svg-icons";
import Button from "../../Button/Button";


export default function HomePage() {
    const [isLoggedIn, setIsLoggedIn] = useState(false);

    useEffect(() => {
        const idToken = sessionStorage.getItem('idToken');
        setIsLoggedIn(!!idToken);
    }, []);

    // Define the saved searches card based on login status
    const savedSearchesCard = isLoggedIn 
        ? {
            icon: <FontAwesomeIcon icon={faBookmark} />,
            title: "Your Saved Searches",
            body: "View and manage your saved search results",
            footer: (
                <Button label="View Searches" linkTo="/Profile" color="grey" variant="secondary" fullWidth/>
            )
        }
        : {
            icon: <FontAwesomeIcon icon={faBookmark} />,
            title: "Save Searches",
            body: "Log in to save and access your search results",
            footer: (
                    <Button label="Log In to Save" linkTo="/Login" color="grey" variant="secondary" fullWidth/>
            )
        };

    return (

        <main className={styles.homeContainer}>
            <section className={styles.welcome} aria-labelledby="welcome-title">
                <img className={styles.welcomeMark} src="/phylodivas-logo-mark.png" alt="" />
                <div className={styles.welcomeContent}>
                    <p className={styles.eyebrow}>Evolutionary genomics workspace</p>
                    <h1 id="welcome-title">Welcome to <span>phyloDIVaS</span></h1>
                    <p className={styles.intro}>
                        Explore annotated RefSeq genomes, inspect genes, and run comparative selection-pressure analyses through one research workflow.
                    </p>
                    <div className={styles.welcomeActions}>
                        <Button label="Explore Genomes" linkTo="/Search" color="blue" variant="primary" />
                        <Button label="Run an Analysis" linkTo="/Analysis" color="grey" variant="secondary" />
                    </div>
                </div>
            </section>
            <section className={styles.quickActions} aria-labelledby="quick-actions-title">
                <div className={styles.sectionHeading}>
                    <h2 id="quick-actions-title">Quick actions</h2>
                    <p>Continue with the part of phyloDIVaS you need.</p>
                </div>
            <CardRow
                cards={[
                    {
                        icon: <FontAwesomeIcon icon={faUsers} />,
                        title: "About Us",
                        body: "Learn about our research goals and team mission",
                        footer: (
                            <Button label="Learn More" linkTo="/About" color="grey" variant="secondary" fullWidth/>
                        )
                    },
                    {
                        icon: <FontAwesomeIcon icon={faSearch} />,
                        title: "Start Searching",
                        body: "Explore genomic sequences and gather data",
                        footer: (
                                <Button label="Search Now" linkTo="/Search" color="grey" variant="secondary" fullWidth/>
                        )
                    },
                    {
                        icon: <FontAwesomeIcon icon={faChartLine} />,
                        title: "Start an Analysis",
                        body: "Compare evolutionary patterns across multiple species",
                        footer: (
                                <Button label="Analyze Now" linkTo="/Analysis" color="grey" variant="secondary" fullWidth/>
                        )
                    },
                    savedSearchesCard
                ]}
            />
            </section>
        </main>
    );
}
