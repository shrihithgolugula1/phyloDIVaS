import styles from "./DisclaimerPage.module.css";

export default function DisclaimerPage() {
    return (
        <div className={styles.disclaimerContainer}>
            <div className={styles.disclaimerCard}>
                <h2 className={styles.title}>Disclaimer</h2>

                <h3>General Information</h3>
                <p>
                    phyloDIVaS is an academic research platform developed by students and faculty at
                    Temple University. All data and visualizations are provided for educational and
                    research purposes only.
                </p>

                <h3>Data Accuracy</h3>
                <p>
                    While we aim to provide reliable and up-to-date information from public databases
                    such as NCBI, we cannot guarantee absolute accuracy. Users should verify any
                    biological or taxonomic data before using it in publication or analysis.
                </p>

                <h3>User Responsibility</h3>
                <p>
                    By using this website, you acknowledge that you are responsible for how you
                    interpret and apply the data. phyloDIVaS and its contributors are not liable for
                    any errors, omissions, or outcomes resulting from the use of this site.
                </p>

                <h3>External Links</h3>
                <p>
                    Our platform may link to third-party resources for convenience. We are not
                    responsible for the content, reliability, or policies of external sites.
                </p>

                <h3>Contact</h3>
                <p>
                    For questions about data sources or usage permissions, please email{" "}
                    <a href="mailto:shrihith.golugula@temple.edu">shrihith.golugula@temple.edu</a>.
                </p>
            </div>
        </div>
    );
}
