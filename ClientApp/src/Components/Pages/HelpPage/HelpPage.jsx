import styles from "./HelpPage.module.css";
import CardRow from "../../Card/CardRow/CardRow";
import LinkTo from "../../Link/Link";

export default function HelpPage() {
    return (
        <div className={styles.helpContainer}>
            <h1 className={styles.pageTitle}>Help & Support</h1>

            <CardRow
                cards={[
                    {
                        title: "Frequently Asked Questions",
                        body: (
                            <div className={styles.cardBody}>
                                <p>
                                    <strong>Q: How do I create an account?</strong><br />
                                    Click “Register” at the top right of the homepage and complete
                                    the sign-up form using your university or organization email.
                                    Once verified, you’ll be able to log in and access saved
                                    searches.
                                </p>
                                <p>
                                    <strong>Q: How do I reset my password?</strong><br />
                                    On the login page, click “Forgot Password.” Enter your
                                    registered email address, and we’ll send you a secure reset link.
                                </p>
                                <p>
                                    <strong>Q: Can I save or export my search results?</strong><br />
                                    Yes. After running a query, click “Save Search” to store your
                                    results, or “Export” to download them as CSV files.
                                </p>
                            </div>
                        ),
                    },
                    {
                        title: "Technical Support",
                        body: (
                            <div className={styles.cardBody}>
                                <p>If you’re experiencing website issues, try the following steps:</p>
                                <ul>
                                    <li>Clear your browser cache and cookies</li>
                                    <li>Use the latest version of Chrome, Edge, or Firefox</li>
                                    <li>
                                        If problems persist, email{" "}
                                        <a href="mailto:shrihith.golugula@temple.edu">
                                            shrihith.golugula@temple.edu
                                        </a>
                                    </li>
                                </ul>
                            </div>
                        ),
                        footer: null,
                    },
                    {
                        title: "Still Need Help?",
                        body: (
                            <div className={styles.cardBody}>
                                <p>
                                    If you did not find an answer here, contact the phyloDIVaS team:
                                </p>
                                <div className={styles.contactBlock}>
                                    <p>shrihith.golugula@temple.edu</p>
                                    <p>Temple University, Philadelphia, PA</p>
                                </div>
                            </div>
                        ),
                        footer: null,
                    },
                ]}
            />
        </div>
    );
}
